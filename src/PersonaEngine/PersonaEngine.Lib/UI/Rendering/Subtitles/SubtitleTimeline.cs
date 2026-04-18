using System.Numerics;
using FontStashSharp;

namespace PersonaEngine.Lib.UI.Rendering.Subtitles;

/// <summary>
///     Manages active subtitle segments, updates animation state,
///     determines visible lines, and calculates their positions.
/// </summary>
public class SubtitleTimeline
{
    private readonly List<SubtitleSegment> _activeSegments = new();

    private float _bottomMargin;

    private FSColor _highlightColor;

    private float _interSegmentSpacing;

    private readonly float _lineSpacing;

    private readonly Lock _lock = new();

    private int _maxVisibleLines;

    private FSColor _normalColor;

    private readonly IWordAnimator _wordAnimator;

    public SubtitleTimeline(
        int maxVisibleLines,
        float bottomMargin,
        float lineSpacing,
        float interSegmentSpacing,
        IWordAnimator wordAnimator,
        FSColor highlightColor,
        FSColor normalColor
    )
    {
        _maxVisibleLines = Math.Max(1, maxVisibleLines);
        _bottomMargin = bottomMargin;
        _lineSpacing = lineSpacing;
        _interSegmentSpacing = interSegmentSpacing;
        _wordAnimator = wordAnimator;
        _highlightColor = highlightColor;
        _normalColor = normalColor;
    }

    public int ActiveSegmentCount
    {
        get
        {
            lock (_lock)
            {
                return _activeSegments.Count;
            }
        }
    }

    public void AddSegment(SubtitleSegment segment)
    {
        lock (_lock)
        {
            _activeSegments.Add(segment);
        }
    }

    public void RemoveSegment(Guid segmentId)
    {
        lock (_lock)
        {
            for (var i = _activeSegments.Count - 1; i >= 0; i--)
            {
                if (_activeSegments[i].Id == segmentId)
                {
                    _activeSegments.RemoveAt(i);
                    return;
                }
            }
        }
    }

    public void ExpireOldSegments(float currentTime, float bufferSeconds = 1.0f)
    {
        lock (_lock)
        {
            var cutoff = currentTime - bufferSeconds;
            for (var i = _activeSegments.Count - 1; i >= 0; i--)
            {
                if (_activeSegments[i].EstimatedEndTime < cutoff)
                {
                    _activeSegments.RemoveAt(i);
                }
            }
        }
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            _activeSegments.Clear();
        }
    }

    /// <summary>
    ///     Updates the maximum number of visible caption lines. Takes effect on the
    ///     next <see cref="GetVisibleLinesAndPosition"/> call; in-flight segments are
    ///     unaffected.
    /// </summary>
    public void SetMaxVisibleLines(int value)
    {
        lock (_lock)
        {
            _maxVisibleLines = Math.Max(1, value);
        }
    }

    /// <summary>
    ///     Updates the distance from the bottom edge of the canvas, in pixels.
    /// </summary>
    public void SetBottomMargin(float value)
    {
        lock (_lock)
        {
            _bottomMargin = value;
        }
    }

    /// <summary>
    ///     Updates the vertical gap between stacked caption segments, in pixels.
    /// </summary>
    public void SetInterSegmentSpacing(float value)
    {
        lock (_lock)
        {
            _interSegmentSpacing = value;
        }
    }

    /// <summary>
    ///     Updates the normal and highlight colors used to tint word animations.
    ///     Affects new segment updates from the next <see cref="Update"/> call;
    ///     already-rendered words retain their last computed color until the next
    ///     animation tick touches them.
    /// </summary>
    public void UpdateColors(FSColor normal, FSColor highlight)
    {
        lock (_lock)
        {
            _normalColor = normal;
            _highlightColor = highlight;
        }
    }

    /// <summary>
    ///     Updates animation progress, scale, and color for all words in active segments.
    /// </summary>
    public void Update(float currentTime)
    {
        lock (_lock)
        {
            foreach (var segment in _activeSegments)
            {
                foreach (var line in segment.Lines)
                {
                    foreach (var word in line.Words)
                    {
                        UpdateWordAnimation(word, currentTime);
                    }
                }
            }
        }
    }

    /// <summary>
    ///     Runs the given action while holding the internal lock.
    ///     Use this to synchronize external mutations (e.g. UpdateSegment)
    ///     with render-thread reads.
    /// </summary>
    public void RunLocked(Action action)
    {
        lock (_lock)
        {
            action();
        }
    }

    /// <summary>
    ///     Collects visible lines and calculates their screen positions.
    ///     Writes results into the provided output list to avoid allocation.
    /// </summary>
    public void GetVisibleLinesAndPosition(
        float currentTime,
        int viewportWidth,
        int viewportHeight,
        List<SubtitleLine> output
    )
    {
        output.Clear();

        lock (_lock)
        {
            CollectVisibleLines(currentTime, output);
            output.Reverse();
            PositionLines(output, viewportWidth, viewportHeight);
        }
    }

    private void CollectVisibleLines(float currentTime, List<SubtitleLine> output)
    {
        for (var i = _activeSegments.Count - 1; i >= 0; i--)
        {
            var segment = _activeSegments[i];

            if (segment.AbsoluteStartTime > currentTime)
            {
                continue;
            }

            for (var j = segment.Lines.Count - 1; j >= 0; j--)
            {
                var line = segment.Lines[j];
                if (line.Words.Count > 0 && line.Words[0].HasStarted(currentTime))
                {
                    output.Add(line);
                    if (output.Count >= _maxVisibleLines)
                    {
                        return;
                    }
                }
            }
        }
    }

    private void PositionLines(List<SubtitleLine> lines, int viewportWidth, int viewportHeight)
    {
        var currentBaselineY = viewportHeight - _bottomMargin;

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var line = lines[i];
            line.BaselineY = currentBaselineY;

            var currentX = (viewportWidth - line.TotalWidth) / 2.0f;

            foreach (var word in line.Words)
            {
                var wordCenterX = currentX + word.Size.X / 2.0f;
                var wordCenterY = currentBaselineY - _lineSpacing / 2.0f;

                word.Position = new Vector2(wordCenterX, wordCenterY);
                currentX += word.Size.X;
            }

            currentBaselineY -= _lineSpacing;

            if (i > 0 && lines[i - 1].SegmentIndex != line.SegmentIndex)
            {
                currentBaselineY -= _interSegmentSpacing;
            }
        }
    }

    /// <summary>
    ///     Minimum duration for animation purposes. Short words (like "is", "a")
    ///     may have CTC durations of 50-80ms — too fast for a visible highlight.
    ///     This floor ensures every word's animation is perceptible.
    /// </summary>
    private const float MinAnimationDuration = 0.2f;

    private void UpdateWordAnimation(SubtitleWordInfo word, float currentTime)
    {
        if (word.HasStarted(currentTime))
        {
            // Use at least MinAnimationDuration so fast words still get a visible highlight
            var animDuration = Math.Max(word.Duration, MinAnimationDuration);
            word.AnimationProgress =
                currentTime >= word.AbsoluteStartTime + animDuration
                    ? 1.0f
                    : Math.Clamp((currentTime - word.AbsoluteStartTime) / animDuration, 0.0f, 1.0f);
        }
        else
        {
            word.AnimationProgress = 0.0f;
        }

        word.CurrentScale = _wordAnimator.CalculateScale(word.AnimationProgress);
        word.CurrentColor = _wordAnimator.CalculateColor(
            _highlightColor,
            _normalColor,
            word.AnimationProgress
        );
    }
}
