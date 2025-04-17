// SubtitleRenderer.cs

using System.Collections.Concurrent;
using System.Drawing;

using FontStashSharp;

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
///     Main class responsible for rendering subtitles using the revised system.
///     Integrates with audio events, processes segments, manages the timeline, and draws text.
/// </summary>
public class SubtitleRenderer : IRenderComponent
{
    private readonly IAudioProgressNotifier _audioNotifier;

    private readonly SubtitleOptions _config;

    private readonly FontProvider _fontProvider;

    private readonly FSColor _highlightColor;

    private readonly FSColor _normalColor;

    private readonly ConcurrentQueue<AudioSegment> _pendingSegments = new();

    private TimeSpan _currentPlaybackTime = TimeSpan.Zero;

    private GL? _gl;

    private bool _isDisposed = false;

    private List<SubtitleLine> _linesToRender = new();

    private Task _processingTask = Task.CompletedTask;

    private SubtitleProcessor _subtitleProcessor;

    private SubtitleTimeline _subtitleTimeline;

    private TextMeasurer _textMeasurer;

    private TextRenderer _textRenderer;

    private int _viewportHeight;

    private int _viewportWidth;

    private IWordAnimator _wordAnimator;

    public SubtitleRenderer(
        IOptions<SubtitleOptions> configOptions,
        IAudioProgressNotifier    audioNotifier,
        FontProvider              fontProvider)
    {
        _config        = configOptions.Value;
        _audioNotifier = audioNotifier;
        _fontProvider  = fontProvider;

        var normalColorSys = ColorTranslator.FromHtml(_config.Color);
        _normalColor = new FSColor(normalColorSys.R, normalColorSys.G, normalColorSys.B, normalColorSys.A);
        var hColorSys = ColorTranslator.FromHtml(_config.HighlightColor);
        _highlightColor = new FSColor(hColorSys.R, hColorSys.G, hColorSys.B, hColorSys.A);

        SubscribeToAudioNotifier();
    }

    public void Dispose()
    {
        if ( _isDisposed )
        {
            return;
        }

        _isDisposed = true;

        UnsubscribeFromAudioNotifier();
        
        _pendingSegments.Clear();

        _linesToRender.Clear();

        GC.SuppressFinalize(this);
    }

    public void Initialize(GL gl, IView view, IInputContext input)
    {
        _gl             = gl;
        _viewportWidth  = view.Size.X;
        _viewportHeight = view.Size.Y;

        var fontSystem = _fontProvider.GetFontSystem(_config.Font);
        var font       = fontSystem.GetFont(_config.FontSize);

        _textMeasurer      = new TextMeasurer(font, _config.SideMargin, _viewportWidth, _viewportHeight);
        _subtitleProcessor = new SubtitleProcessor(_textMeasurer, _config.AnimationDuration);
        _wordAnimator      = new PopAnimator();

        _subtitleTimeline = new SubtitleTimeline(
                                                 _config.MaxVisibleLines,
                                                 _config.BottomMargin,
                                                 _textMeasurer.LineHeight + 0.5f,
                                                 _config.InterSegmentSpacing,
                                                 _textMeasurer,
                                                 _wordAnimator,
                                                 _highlightColor,
                                                 _normalColor
                                                );

        _textRenderer = new TextRenderer(_gl);

        Resize();
    }
    
    public bool UseSpout { get; } = true;

    public string SpoutTarget { get; } = "Live2D";

    public int Priority { get; } = -100;

    public void Update(float deltaTime)
    {
        if ( _isDisposed )
        {
            return;
        }

        var currentTime = (float)_currentPlaybackTime.TotalSeconds;

        _subtitleTimeline.Update(currentTime);

        _linesToRender = _subtitleTimeline.GetVisibleLinesAndPosition(currentTime, _viewportWidth, _viewportHeight);
    }

    public void Render(float deltaTime)
    {
        if ( _isDisposed || _gl == null )
        {
            return;
        }

        _textRenderer.Begin();

        foreach ( var line in _linesToRender )
        {
            foreach ( var word in line.Words )
            {
                if ( word.AnimationProgress > 0 || word.IsActive((float)_currentPlaybackTime.TotalSeconds) )
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

        _textRenderer.End();
    }

    public void Resize()
    {
        _viewportWidth  = _config.Width;
        _viewportHeight = _config.Height;

        _textMeasurer.UpdateViewport(_viewportWidth, _viewportHeight);
        _textRenderer.OnViewportChanged(_viewportWidth, _viewportHeight);
    }

    private void SubscribeToAudioNotifier()
    {
        _audioNotifier.ChunkPlaybackStarted += OnChunkPlaybackStarted;
        _audioNotifier.ChunkPlaybackEnded   += OnChunkPlaybackEnded;
        _audioNotifier.PlaybackProgress     += OnPlaybackProgress;
    }

    private void UnsubscribeFromAudioNotifier()
    {
        _audioNotifier.ChunkPlaybackStarted -= OnChunkPlaybackStarted;
        _audioNotifier.ChunkPlaybackEnded   -= OnChunkPlaybackEnded;
        _audioNotifier.PlaybackProgress     -= OnPlaybackProgress;
    }
    
    private void OnChunkPlaybackStarted(object? sender, AudioChunkPlaybackStartedEvent e)
    {
        _pendingSegments.Enqueue(e.Chunk);

        if ( _processingTask.IsCompleted )
        {
            _processingTask = Task.Run(ProcessPendingSegmentsAsync);
        }
    }

    private void OnChunkPlaybackEnded(object? sender, AudioChunkPlaybackEndedEvent e) { _subtitleTimeline.RemoveSegment(e.Chunk); }

    private void OnPlaybackProgress(object? sender, AudioPlaybackProgressEvent e) { _currentPlaybackTime = e.CurrentPlaybackTime; }

    private void ProcessPendingSegmentsAsync()
    {
        while ( _pendingSegments.TryDequeue(out var audioSegment) )
        {
            try
            {
                var segmentStartTime = (float)0f;

                var processedSegment = _subtitleProcessor.ProcessSegment(audioSegment, segmentStartTime);

                _subtitleTimeline.AddSegment(processedSegment);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing subtitle segment: {ex.Message}");
            }
        }
    }
}