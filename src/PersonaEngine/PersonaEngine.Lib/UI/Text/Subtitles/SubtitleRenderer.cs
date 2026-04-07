using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FontStashSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;
using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.UI.Text.Rendering;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace PersonaEngine.Lib.UI.Text.Subtitles;

/// <summary>
///     Renders subtitles synchronized to audio playback.
///     Integrates with audio events, processes segments, manages the timeline, and draws text.
/// </summary>
public class SubtitleRenderer : IRenderComponent
{
    private readonly IAudioProgressNotifier _audioNotifier;

    private readonly SubtitleOptions _config;

    private readonly FontProvider _fontProvider;

    private readonly FSColor _highlightColor;

    private readonly ILogger<SubtitleRenderer> _logger;

    private readonly FSColor _normalColor;

    /// <summary>
    ///     Maps AudioSegment reference identity → SubtitleSegment.Id for correlating
    ///     started/ended events without relying on record Equals (which breaks when
    ///     audio filters mutate AudioData).
    /// </summary>
    private readonly ConcurrentDictionary<AudioSegment, Guid> _segmentIdMap = new(
        ReferenceEqualityComparer.Instance
    );

    /// <summary>
    ///     Maps SentenceId → (SubtitleSegment.Id, SubtitleSegment) for persistent
    ///     per-sentence segments. Streaming TTS sends many audio chunks per sentence;
    ///     instead of creating a new subtitle segment per chunk, we update the existing one.
    /// </summary>
    private readonly ConcurrentDictionary<
        Guid,
        (Guid SegmentId, SubtitleSegment Segment)
    > _sentenceSegmentMap = new();

    private readonly Channel<SubtitleCommand> _commandChannel =
        Channel.CreateUnbounded<SubtitleCommand>(
            new UnboundedChannelOptions { SingleReader = true }
        );

    private readonly List<SubtitleLine> _linesToRender = new();

    /// <summary>
    ///     Sum of all completed chunk durations. Combined with per-chunk progress,
    ///     produces a monotonically increasing absolute playback time.
    /// </summary>
    private float _cumulativeTimeOffset;

    private TimeSpan _currentChunkPlaybackTime = TimeSpan.Zero;

    private GL? _gl;

    private volatile bool _isDisposed;

    private CancellationTokenSource? _processingCts;

    private Task _processingTask = Task.CompletedTask;

    private SubtitleProcessor _subtitleProcessor = null!;

    private SubtitleTimeline _subtitleTimeline = null!;

    private TextMeasurer _textMeasurer = null!;

    private TextRenderer _textRenderer = null!;

    private int _viewportHeight;

    private int _viewportWidth;

    private IWordAnimator _wordAnimator = null!;

    public SubtitleRenderer(
        IOptions<SubtitleOptions> configOptions,
        IAudioProgressNotifier audioNotifier,
        FontProvider fontProvider,
        ILogger<SubtitleRenderer> logger
    )
    {
        _config = configOptions.Value;
        _audioNotifier = audioNotifier;
        _fontProvider = fontProvider;
        _logger = logger;

        var normalColorSys = ColorTranslator.FromHtml(_config.Color);
        _normalColor = new FSColor(
            normalColorSys.R,
            normalColorSys.G,
            normalColorSys.B,
            normalColorSys.A
        );
        var hColorSys = ColorTranslator.FromHtml(_config.HighlightColor);
        _highlightColor = new FSColor(hColorSys.R, hColorSys.G, hColorSys.B, hColorSys.A);

        SubscribeToAudioNotifier();
    }

    public bool UseSpout { get; } = true;

    public string SpoutTarget { get; } = "Live2D";

    public int Priority { get; } = -100;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        UnsubscribeFromAudioNotifier();
        _commandChannel.Writer.TryComplete();
        _processingCts?.Cancel();

        _segmentIdMap.Clear();
        _sentenceSegmentMap.Clear();
        _linesToRender.Clear();

        GC.SuppressFinalize(this);
    }

    public void Initialize(GL gl, IView view, IInputContext input)
    {
        _gl = gl;
        _viewportWidth = view.Size.X;
        _viewportHeight = view.Size.Y;

        var fontSystem = _fontProvider.GetFontSystem(_config.Font);
        var font = fontSystem.GetFont(_config.FontSize);

        _textMeasurer = new TextMeasurer(font, _config.SideMargin, _viewportWidth, _viewportHeight);
        _subtitleProcessor = new SubtitleProcessor(_textMeasurer, _config.AnimationDuration);
        _wordAnimator = new PopAnimator();

        _subtitleTimeline = new SubtitleTimeline(
            _config.MaxVisibleLines,
            _config.BottomMargin,
            _textMeasurer.LineHeight + 0.5f,
            _config.InterSegmentSpacing,
            _wordAnimator,
            _highlightColor,
            _normalColor
        );

        _textRenderer = new TextRenderer(_gl);

        _processingCts = new CancellationTokenSource();
        _processingTask = Task.Run(() => ProcessCommandLoop(_processingCts.Token));

        Resize();
    }

    public void Update(float deltaTime)
    {
        if (_isDisposed)
        {
            return;
        }

        var currentTime = GetCurrentAbsoluteTime();

        _subtitleTimeline.ExpireOldSegments(currentTime, bufferSeconds: 2.0f);
        _subtitleTimeline.Update(currentTime);
        _subtitleTimeline.GetVisibleLinesAndPosition(
            currentTime,
            _viewportWidth,
            _viewportHeight,
            _linesToRender
        );
    }

    public void Render(float deltaTime)
    {
        if (_isDisposed || _gl == null)
        {
            return;
        }

        _textRenderer.Begin();

        var currentTime = GetCurrentAbsoluteTime();

        _subtitleTimeline.RunLocked(() =>
        {
            foreach (var line in _linesToRender)
            {
                foreach (var word in line.Words)
                {
                    if (word.AnimationProgress > 0 || word.IsActive(currentTime))
                    {
                        _textMeasurer.Font.DrawText(
                            _textRenderer,
                            word.Text,
                            word.Position,
                            word.CurrentColor,
                            scale: word.CurrentScale,
                            origin: word.Size / 2.0f,
                            effect: FontSystemEffect.Stroked,
                            effectAmount: _config.StrokeThickness
                        );
                    }
                }
            }
        });

        _textRenderer.End();
    }

    public void Resize()
    {
        _viewportWidth = _config.Width;
        _viewportHeight = _config.Height;

        _textMeasurer.UpdateViewport(_viewportWidth, _viewportHeight);
        _textRenderer.OnViewportChanged(_viewportWidth, _viewportHeight);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float GetCurrentAbsoluteTime()
    {
        return _cumulativeTimeOffset + (float)_currentChunkPlaybackTime.TotalSeconds;
    }

    private void SubscribeToAudioNotifier()
    {
        _audioNotifier.ChunkPlaybackStarted += OnChunkPlaybackStarted;
        _audioNotifier.ChunkPlaybackEnded += OnChunkPlaybackEnded;
        _audioNotifier.PlaybackProgress += OnPlaybackProgress;
    }

    private void UnsubscribeFromAudioNotifier()
    {
        _audioNotifier.ChunkPlaybackStarted -= OnChunkPlaybackStarted;
        _audioNotifier.ChunkPlaybackEnded -= OnChunkPlaybackEnded;
        _audioNotifier.PlaybackProgress -= OnPlaybackProgress;
    }

    private void OnChunkPlaybackStarted(object? sender, AudioChunkPlaybackStartedEvent e)
    {
        // Advance cumulative offset by the previous chunk's duration, then reset per-chunk time
        _cumulativeTimeOffset += (float)_currentChunkPlaybackTime.TotalSeconds;
        _currentChunkPlaybackTime = TimeSpan.Zero;

        _commandChannel.Writer.TryWrite(new AddSegmentCommand(e.Chunk, _cumulativeTimeOffset));
    }

    private void OnChunkPlaybackEnded(object? sender, AudioChunkPlaybackEndedEvent e)
    {
        // Advance cumulative time to the actual end of this chunk.
        // The audio callback may not emit a final progress event when a buffer
        // finishes (it returns Complete from inside the loop), so _currentChunkPlaybackTime
        // can freeze slightly before the chunk's true duration. This ensures subtitle
        // animations for the last word complete naturally.
        _cumulativeTimeOffset += (float)e.Chunk.DurationInSeconds;
        _currentChunkPlaybackTime = TimeSpan.Zero;

        _commandChannel.Writer.TryWrite(new RemoveSegmentCommand(e.Chunk));
    }

    private void OnPlaybackProgress(object? sender, AudioPlaybackProgressEvent e)
    {
        _currentChunkPlaybackTime = e.CurrentPlaybackTime;
    }

    private async Task ProcessCommandLoop(CancellationToken ct)
    {
        try
        {
            await foreach (var command in _commandChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    switch (command)
                    {
                        case AddSegmentCommand add:
                            ProcessAddSegment(add);
                            break;

                        case RemoveSegmentCommand remove:
                            ProcessRemoveSegment(remove);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing subtitle command");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private void ProcessAddSegment(AddSegmentCommand command)
    {
        var sentenceId = command.AudioSegment.SentenceId;

        // If a segment for this sentence already exists, update it with new timing
        if (
            sentenceId != Guid.Empty
            && _sentenceSegmentMap.TryGetValue(sentenceId, out var existing)
        )
        {
            // Update existing segment's word timings and add new words.
            // Must run under the timeline lock because the render thread
            // iterates the same Words lists during Render/PositionLines.
            _subtitleTimeline.RunLocked(
                () =>
                    _subtitleProcessor.UpdateSegment(
                        existing.Segment,
                        command.AudioSegment,
                        existing.Segment.AbsoluteStartTime
                    )
            );

            _segmentIdMap[command.AudioSegment] = existing.SegmentId;
            return;
        }

        // New sentence starting — clean up any previous sentence's persistent segment
        if (sentenceId != Guid.Empty)
        {
            foreach (var (oldSentenceId, oldEntry) in _sentenceSegmentMap)
            {
                if (oldSentenceId != sentenceId)
                {
                    _subtitleTimeline.RemoveSegment(oldEntry.SegmentId);
                }
            }

            _sentenceSegmentMap.Clear();
        }

        // First chunk for this sentence: create new segment
        var processedSegment = _subtitleProcessor.ProcessSegment(
            command.AudioSegment,
            command.AbsoluteStartTime
        );

        _segmentIdMap[command.AudioSegment] = processedSegment.Id;
        _subtitleTimeline.AddSegment(processedSegment);

        // Track for future updates from the same sentence
        if (sentenceId != Guid.Empty)
        {
            _sentenceSegmentMap[sentenceId] = (processedSegment.Id, processedSegment);
        }
    }

    private void ProcessRemoveSegment(RemoveSegmentCommand command)
    {
        var sentenceId = command.AudioSegment.SentenceId;

        // If this chunk belongs to a persistent sentence segment, don't remove it.
        // The segment stays alive across chunks. It will be cleaned up by
        // ExpireOldSegments when playback moves past it, or when a new sentence starts.
        if (sentenceId != Guid.Empty && _sentenceSegmentMap.ContainsKey(sentenceId))
        {
            _segmentIdMap.TryRemove(command.AudioSegment, out _);
            return;
        }

        if (_segmentIdMap.TryRemove(command.AudioSegment, out var segmentId))
        {
            _subtitleTimeline.RemoveSegment(segmentId);
        }
    }

    private abstract record SubtitleCommand;

    private sealed record AddSegmentCommand(AudioSegment AudioSegment, float AbsoluteStartTime)
        : SubtitleCommand;

    private sealed record RemoveSegmentCommand(AudioSegment AudioSegment) : SubtitleCommand;
}
