using System.Numerics;

using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.UI.Text.Subtitles;

public class SegmentManager
{
    private readonly List<ProcessedSubtitleSegment> _activeSegments = new();

    private readonly Lock _lock = new();

    private readonly int _maxVisibleLines;

    public SegmentManager(int maxVisibleLines) { _maxVisibleLines = Math.Max(1, maxVisibleLines); }

    public void AddSegment(ProcessedSubtitleSegment segment)
    {
        lock (_lock)
        {
            _activeSegments.Add(segment);
        }
    }

    public void RemoveSegment(AudioSegment segmentToRemove)
    {
        lock (_lock)
        {
            _activeSegments.RemoveAll(s => ReferenceEquals(s.OriginalSegment, segmentToRemove));
        }
    }

    public void Update(float currentTime, float deltaTime)
    {
        lock (_lock) // Ensure thread safety when iterating
        {
            foreach ( var segment in _activeSegments )
            {
                foreach ( var line in segment.Lines )
                {
                    foreach ( var word in line.Words )
                    {
                        if ( word.HasStarted(currentTime) )
                        {
                            if ( !word.IsComplete(currentTime) )
                            {
                                // Calculate progress based on how much time has passed since start
                                var elapsed = currentTime - word.StartTime;
                                word.AnimationProgress = Math.Clamp(elapsed / word.Duration, 0f, 1f);
                            }
                            else
                            {
                                word.AnimationProgress = 1f; // Ensure it stays at 1 when complete
                            }
                        }
                        else
                        {
                            word.AnimationProgress = 0f; // Not started yet
                        }
                    }
                }
            }
        }
    }

    public List<ProcessedSubtitleLine> GetVisibleLines(float currentTime)
    {
        var visibleLines = new List<ProcessedSubtitleLine>();
        lock (_lock)
        {
            for ( var i = _activeSegments.Count - 1; i >= 0; i-- )
            {
                var segment = _activeSegments[i];

                for ( var j = segment.Lines.Count - 1; j >= 0; j-- )
                {
                    var line = segment.Lines[j];

                    if ( line.Words.Count != 0 && line.Words[0].HasStarted(currentTime) )
                    {
                        visibleLines.Add(line);
                        if ( visibleLines.Count >= _maxVisibleLines )
                        {
                            goto EndLoop;
                        }
                    }
                }
            }
        }

        EndLoop:
        visibleLines.Reverse();

        return visibleLines;
    }

    public void PositionLines(
        List<ProcessedSubtitleLine> lines,
        int                         viewportWidth,
        int                         viewportHeight,
        float                       bottomMargin,
        float                       lineHeight,
        float                       interSegmentSpacing)
    {
        var currentY = viewportHeight - bottomMargin - lineHeight;

        for ( var i = lines.Count - 1; i >= 0; i-- )
        {
            var line           = lines[i];
            var totalLineWidth = line.LineWidth;
            var currentX       = (viewportWidth - totalLineWidth) / 2f;

            foreach ( var word in line.Words )
            {
                var wordCenterX = currentX + word.Size.X / 2f;
                var wordCenterY = currentY + lineHeight / 2f;
                word.Position =  new Vector2(wordCenterX, wordCenterY);
                currentX      += word.Size.X;
            }

            currentY -= lineHeight + interSegmentSpacing;
        }
    }
}