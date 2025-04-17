using System.Collections.Concurrent;
using System.Drawing;
using System.Text;

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

public class SubtitleRenderer : IRenderComponent
{
    private readonly IAudioProgressNotifier _audioNotifier;

    private readonly SubtitleOptions _config;

    private readonly FontProvider _fontProvider;

    private readonly ConcurrentQueue<AudioSegment> _pendingSegments = new();

    private IAnimationStrategy _animationStrategy;

    private TimeSpan _currentPlaybackTime = TimeSpan.Zero;

    private bool _disposed;

    private DynamicSpriteFont _font;

    private GL _gl;

    private FSColor _highlightColor;

    private TextLayoutCache _layoutCache;

    private FSColor _normalColor;

    private SegmentManager _segmentManager;

    private TextRenderer _textRenderer;

    private int _viewportWidth,
                _viewportHeight;

    private List<ProcessedSubtitleLine> _visibleLines = new();

    public SubtitleRenderer(IOptions<SubtitleOptions> config, FontProvider fontProvider, IAudioProgressNotifier audioNotifier)
    {
        _config        = config.Value;
        _fontProvider  = fontProvider;
        _audioNotifier = audioNotifier;

        SubscribeToAudioNotifier();
    }

    public bool UseSpout => true;

    public string SpoutTarget => "Live2D";

    public int Priority => -100;

    public void Initialize(GL gl, IView view, IInputContext input)
    {
        _gl           = gl;
        _textRenderer = new TextRenderer(_gl);

        var fontSystem = _fontProvider.GetFontSystem(_config.Font);
        _font = fontSystem.GetFont(_config.FontSize);

        var normalColor = ColorTranslator.FromHtml(_config.Color);
        _normalColor = new FSColor(normalColor.R, normalColor.G, normalColor.B, normalColor.A);
        var hColor = ColorTranslator.FromHtml(_config.HighlightColor);
        _highlightColor = new FSColor(hColor.R, hColor.G, hColor.B, hColor.A);

        _layoutCache       = new TextLayoutCache(_font, _config.SideMargin, _config.Width, _config.Height);
        _segmentManager    = new SegmentManager(_config.MaxVisibleLines);
        _animationStrategy = new PopAnimation();

        Resize();
    }

    public void Update(float deltaTime)
    {
        var currentTime = (float)_currentPlaybackTime.TotalSeconds;

        _segmentManager.Update(currentTime, deltaTime);
        _visibleLines = _segmentManager.GetVisibleLines(currentTime);

        _segmentManager.PositionLines(
                                      _visibleLines,
                                      _viewportWidth,
                                      _viewportHeight,
                                      _config.BottomMargin,
                                      _layoutCache.LineHeight,
                                      _config.InterSegmentSpacing);
    }

    public void Render(float deltaTime)
    {
        var currentTime = (float)_currentPlaybackTime.TotalSeconds;

        _textRenderer.Begin();

        foreach ( var line in _visibleLines )
        {
            foreach ( var word in line.Words )
            {
                if ( !word.HasStarted(currentTime) )
                {
                    continue;
                }

                var progress = word.AnimationProgress;
                var scale    = _animationStrategy.CalculateScale(progress);
                var color    = _animationStrategy.CalculateColor(_highlightColor, _normalColor, progress);

                _font.DrawText(
                               _textRenderer,
                               word.Text,
                               word.Position,
                               color,
                               scale: scale,
                               effect: FontSystemEffect.Stroked,
                               effectAmount: 3,
                               origin: word.Size / 2);
            }
        }

        _textRenderer.End();
    }

    public void Dispose()
    {
        // Context is destroyed anyway when app closes.

        return;

        if ( !_disposed )
        {
            _textRenderer.Dispose();

            _audioNotifier.ChunkPlaybackStarted -= OnChunkPlaybackStarted;
            _audioNotifier.ChunkPlaybackEnded   -= OnChunkPlaybackEnded;
            _audioNotifier.PlaybackProgress     -= OnPlaybackProgress;

            _pendingSegments.Clear();
            _visibleLines.Clear();

            _disposed = true;
        }
    }

    public void Resize()
    {
        _viewportWidth  = _config.Width;
        _viewportHeight = _config.Height;
        _layoutCache.UpdateViewport(_viewportWidth, _viewportHeight);

        if ( _visibleLines.Count != 0 )
        {
            _segmentManager.PositionLines(
                                          _visibleLines,
                                          _viewportWidth,
                                          _viewportHeight,
                                          _config.BottomMargin,
                                          _layoutCache.LineHeight,
                                          _config.InterSegmentSpacing);
        }

        _textRenderer.OnViewportChanged(_viewportWidth, _viewportHeight);
    }

    private void SubscribeToAudioNotifier()
    {
        _audioNotifier.ChunkPlaybackStarted -= OnChunkPlaybackStarted;
        _audioNotifier.ChunkPlaybackEnded   -= OnChunkPlaybackEnded;
        _audioNotifier.PlaybackProgress     -= OnPlaybackProgress;

        _audioNotifier.ChunkPlaybackStarted += OnChunkPlaybackStarted;
        _audioNotifier.ChunkPlaybackEnded   += OnChunkPlaybackEnded;
        _audioNotifier.PlaybackProgress     += OnPlaybackProgress;
    }

    private void OnChunkPlaybackStarted(object? sender, AudioChunkPlaybackStartedEvent e)
    {
        _pendingSegments.Enqueue(e.Chunk);
        Task.Run(ProcessPendingSegmentsAsync);
    }

    private void OnChunkPlaybackEnded(object? sender, AudioChunkPlaybackEndedEvent e) { _segmentManager.RemoveSegment(e.Chunk); }

    private void OnPlaybackProgress(object? sender, AudioPlaybackProgressEvent e) { _currentPlaybackTime = e.CurrentPlaybackTime; }

    private async Task ProcessPendingSegmentsAsync()
    {
        while ( _pendingSegments.TryDequeue(out var audioSegment) )
        {
            var processedSegment = await Task.Run(() => ProcessAudioSegment(audioSegment));
            _segmentManager.AddSegment(processedSegment);
        }
    }

    private ProcessedSubtitleSegment ProcessAudioSegment(AudioSegment audioSegment)
    {
        var basePlaybackTime          = (float)_currentPlaybackTime.TotalSeconds;
        var segmentEffectiveStartTime = basePlaybackTime;

        var textLength  = audioSegment.Tokens.Sum(t => t.Text.Length + t.Whitespace.Length);
        var textBuilder = new StringBuilder(textLength);
        foreach ( var token in audioSegment.Tokens )
        {
            textBuilder.Append(token.Text);
            textBuilder.Append(token.Whitespace);
        }

        var fullText = textBuilder.ToString();
        var segment  = new ProcessedSubtitleSegment(audioSegment, fullText, segmentEffectiveStartTime);

        var currentLine          = new ProcessedSubtitleLine(0);
        var currentLineWidth     = 0f;
        var defaultTokenDuration = _config.AnimationDuration > 0 ? _config.AnimationDuration : 0.5f;

        for ( var i = 0; i < audioSegment.Tokens.Count; i++ )
        {
            var token       = audioSegment.Tokens[i];
            var displayText = token.Text + token.Whitespace;
            var tokenSize   = _layoutCache.MeasureText(displayText);

            if ( currentLineWidth > 0 && currentLineWidth + tokenSize.X > _layoutCache.AvailableWidth )
            {
                segment.Lines.Add(currentLine);
                currentLine      = new ProcessedSubtitleLine(segment.Lines.Count);
                currentLineWidth = 0f;
            }

            float wordStartTimeOffset;
            float wordDuration;

            // Case 1: Token has explicit start and end timestamps.
            if ( token is { StartTs: not null, EndTs: not null } )
            {
                wordStartTimeOffset = (float)token.StartTs.Value;
                wordDuration        = Math.Max(0.01f, (float)(token.EndTs.Value - token.StartTs.Value));
            }
            // Case 2: Token has only start timestamp.
            else if ( token.StartTs.HasValue )
            {
                wordStartTimeOffset = (float)token.StartTs.Value;
                // Estimate duration: time until next token starts, or default duration.
                if ( i + 1 < audioSegment.Tokens.Count && audioSegment.Tokens[i + 1].StartTs.HasValue )
                {
                    wordDuration = Math.Max(0.01f, (float)audioSegment.Tokens[i + 1].StartTs!.Value - wordStartTimeOffset);
                }
                else
                {
                    wordDuration = defaultTokenDuration;
                }
            }
            // Case 3: Token has only end timestamp (less common, treat start as previous end).
            else if ( token.EndTs.HasValue )
            {
                wordDuration = defaultTokenDuration; // Can't calculate duration accurately
                if ( i > 0 )
                {
                    var prevToken = audioSegment.Tokens[i - 1];
                    if ( prevToken.EndTs.HasValue )
                    {
                        wordStartTimeOffset = (float)prevToken.EndTs.Value;
                    }
                    else if ( prevToken.StartTs.HasValue )
                    {
                        var prevDuration = defaultTokenDuration;
                        if ( i > 0 && audioSegment.Tokens[i - 1].EndTs.HasValue )
                        {
                            prevDuration = Math.Max(0.01f, (float)(audioSegment.Tokens[i - 1].EndTs!.Value - audioSegment.Tokens[i - 1].StartTs!.Value));
                        }
                        else if ( i < audioSegment.Tokens.Count - 1 && audioSegment.Tokens[i + 1].StartTs.HasValue )
                        {
                            prevDuration = Math.Max(0.01f, (float)(audioSegment.Tokens[i + 1].StartTs!.Value - prevToken.StartTs.Value));
                        }

                        wordStartTimeOffset = (float)prevToken.StartTs.Value + prevDuration;
                    }
                    else
                    {
                        wordStartTimeOffset = (currentLine.Words.LastOrDefault()?.StartTime ?? segmentEffectiveStartTime) + (currentLine.Words.LastOrDefault()?.Duration ?? 0) - segmentEffectiveStartTime; // Relative offset
                    }
                }
                else
                {
                    wordStartTimeOffset = 0f;
                }

                if ( segmentEffectiveStartTime + wordStartTimeOffset > (float)token.EndTs.Value )
                {
                    wordDuration = 0.01f;
                }
                // This case is tricky, maybe estimate based on EndTs - StartTs? But StartTs is unknown.
                // Sticking to default duration might be safest.
            }
            // Case 4: Token has no timestamps.
            else
            {
                wordDuration = defaultTokenDuration;
                if ( i > 0 )
                {
                    var prevToken = audioSegment.Tokens[i - 1];
                    if ( prevToken.EndTs.HasValue )
                    {
                        wordStartTimeOffset = (float)prevToken.EndTs.Value;
                    }
                    else if ( prevToken.StartTs.HasValue )
                    {
                        var prevDuration = defaultTokenDuration;
                        if ( prevToken.EndTs.HasValue )
                        {
                            prevDuration = Math.Max(0.01f, (float)(prevToken.EndTs.Value - prevToken.StartTs.Value));
                        }
                        else if ( i < audioSegment.Tokens.Count - 1 && audioSegment.Tokens[i + 1].StartTs.HasValue )
                        {
                            prevDuration = Math.Max(0.01f, (float)(audioSegment.Tokens[i + 1].StartTs!.Value - prevToken.StartTs.Value));
                        }

                        wordStartTimeOffset = (float)prevToken.StartTs.Value + prevDuration;
                    }
                    else
                    {
                        wordStartTimeOffset = (currentLine.Words.LastOrDefault()?.StartTime ?? segmentEffectiveStartTime) + (currentLine.Words.LastOrDefault()?.Duration ?? 0) - segmentEffectiveStartTime; // Relative offset
                    }
                }
                else
                {
                    wordStartTimeOffset = 0f;
                }
            }

            wordDuration = Math.Max(0.01f, wordDuration);
            var word = new ProcessedWord(
                                         displayText,
                                         segmentEffectiveStartTime + wordStartTimeOffset,
                                         wordDuration,
                                         tokenSize);

            currentLine.AddWord(word);
            currentLineWidth += tokenSize.X;
        }

        if ( currentLine.Words.Count != 0 )
        {
            segment.Lines.Add(currentLine);
        }

        return segment;
    }
}