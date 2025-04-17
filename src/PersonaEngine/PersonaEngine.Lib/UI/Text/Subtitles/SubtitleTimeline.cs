using System.Numerics;

using FontStashSharp;

namespace PersonaEngine.Lib.UI.Text.Subtitles;

/// <summary>
///     Manages active subtitle segments, updates their state based on time,
///     determines visible lines, and calculates their positions.
/// </summary>
public class SubtitleTimeline
{
    private readonly List<SubtitleSegment> _activeSegments = new();

    private readonly float _bottomMargin;

    private readonly FSColor _highlightColor;

    private readonly float _interSegmentSpacing;

    private readonly float _lineSpacing;

    private readonly Lock _lock = new();

    private readonly int _maxVisibleLines;

    private readonly FSColor _normalColor;

    private readonly TextMeasurer _textMeasurer;

    private readonly List<SubtitleLine> _visibleLinesCache;

    private readonly IWordAnimator _wordAnimator;

    public SubtitleTimeline(
        int           maxVisibleLines,
        float         bottomMargin,
        float         lineSpacing,
        float         interSegmentSpacing,
        TextMeasurer  textMeasurer,
        IWordAnimator wordAnimator,
        FSColor       highlightColor,
        FSColor       normalColor)
    {
        _maxVisibleLines     = Math.Max(1, maxVisibleLines);
        _bottomMargin        = bottomMargin;
        _lineSpacing         = lineSpacing;
        _interSegmentSpacing = interSegmentSpacing;
        _textMeasurer        = textMeasurer;
        _wordAnimator        = wordAnimator;
        _highlightColor      = highlightColor;
        _normalColor         = normalColor;

        _visibleLinesCache = new List<SubtitleLine>(_maxVisibleLines * 2);
    }

    public void AddSegment(SubtitleSegment segment)
    {
        lock (_lock)
        {
            _activeSegments.Add(segment);
        }
    }

    public void RemoveSegment(object originalSegmentIdentifier)
    {
        lock (_lock)
        {
            _activeSegments.RemoveAll(s => ReferenceEquals(s.OriginalAudioSegment, originalSegmentIdentifier) || s.OriginalAudioSegment.Equals(originalSegmentIdentifier));

            // TODO: If using object pooling for segments/lines/words, return them to the pool here.
        }
    }

    /// <summary>
    ///     Updates the animation progress and state of words in active segments.
    /// </summary>
    public void Update(float currentTime)
    {
        lock (_lock)
        {
            for ( var i = _activeSegments.Count - 1; i >= 0; i-- )
            {
                var segment = _activeSegments[i];

                // Optimization: Potentially skip segments that are entirely in the past?
                // if (segment.EstimatedEndTime < currentTime - someBuffer) continue;
                // Optimization: Potentially skip segments entirely in the future?
                // if (segment.AbsoluteStartTime > currentTime + someBuffer) continue;

                for ( var j = 0; j < segment.Lines.Count; j++ )
                {
                    var line = segment.Lines[j];
                    // Use ref local for structs to avoid copying if SubtitleWordInfo is a struct
                    // Requires Words to be an array or Span<T> for direct ref access.
                    // With List<T>, we modify the copy and then update the list element.
                    for ( var k = 0; k < line.Words.Count; k++ )
                    {
                        var word = line.Words[k];

                        if ( word.HasStarted(currentTime) )
                        {
                            if ( word.IsComplete(currentTime) )
                            {
                                word.AnimationProgress = 1.0f;
                            }
                            else
                            {
                                var elapsed = currentTime - word.AbsoluteStartTime;
                                word.AnimationProgress = Math.Clamp(elapsed / word.Duration, 0.0f, 1.0f);
                            }
                        }
                        else
                        {
                            word.AnimationProgress = 0.0f;
                        }

                        word.CurrentScale = _wordAnimator.CalculateScale(word.AnimationProgress);
                        word.CurrentColor = _wordAnimator.CalculateColor(_highlightColor, _normalColor, word.AnimationProgress);

                        line.Words[k] = word;
                    }
                }
            }
        }
    }

    public List<SubtitleLine> GetVisibleLinesAndPosition(float currentTime, int viewportWidth, int viewportHeight)
    {
        _visibleLinesCache.Clear();

        lock (_lock)
        {
            for ( var i = _activeSegments.Count - 1; i >= 0; i-- )
            {
                var segment = _activeSegments[i];

                // Optimization: If segment hasn't started, none of its lines are visible.
                if ( segment.AbsoluteStartTime > currentTime )
                {
                    continue;
                }

                for ( var j = segment.Lines.Count - 1; j >= 0; j-- )
                {
                    var line = segment.Lines[j];

                    if ( line.Words.Count > 0 && line.Words[0].HasStarted(currentTime) )
                    {
                        _visibleLinesCache.Add(line);
                        if ( _visibleLinesCache.Count >= _maxVisibleLines )
                        {
                            goto FoundEnoughLines;
                        }
                    }

                    // Optimization: If the first word of this line hasn't started,
                    // earlier lines in the *same segment* also won't have started yet.
                    // (Assumes words within a line are chronologically ordered).
                    // else if (line.Words.Count > 0)
                    // {
                    //     break; // Stop checking lines in this segment
                    // }
                }
            }
        }

        FoundEnoughLines:

        _visibleLinesCache.Reverse();

        var currentBaselineY = viewportHeight - _bottomMargin;

        for ( var i = _visibleLinesCache.Count - 1; i >= 0; i-- )
        {
            var line = _visibleLinesCache[i];
            line.BaselineY = currentBaselineY;

            var currentX = (viewportWidth - line.TotalWidth) / 2.0f;

            for ( var k = 0; k < line.Words.Count; k++ )
            {
                var word = line.Words[k];

                var wordCenterX = currentX + word.Size.X / 2.0f;
                var wordCenterY = currentBaselineY - _textMeasurer.LineHeight / 2.0f;

                word.Position =  new Vector2(wordCenterX, wordCenterY);
                line.Words[k] =  word;
                currentX      += word.Size.X;
            }

            currentBaselineY -= _lineSpacing;

            if ( i > 0 && _visibleLinesCache[i - 1].SegmentIndex != line.SegmentIndex )
            {
                currentBaselineY -= _interSegmentSpacing;
            }
        }

        return _visibleLinesCache;
    }
}