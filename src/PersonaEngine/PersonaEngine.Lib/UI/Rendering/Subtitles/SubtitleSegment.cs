namespace PersonaEngine.Lib.UI.Rendering.Subtitles;

/// <summary>
///     Represents a processed audio segment, broken down into lines and words
///     with calculated timing and layout information ready for the timeline.
/// </summary>
public class SubtitleSegment(float absoluteStartTime, string fullText)
{
    public Guid Id { get; } = Guid.NewGuid();

    public float AbsoluteStartTime { get; } = absoluteStartTime;

    public string FullText { get; } = fullText;

    public List<SubtitleLine> Lines { get; } = new();

    public float EstimatedEndTime { get; set; } = absoluteStartTime;

    public void AddLine(SubtitleLine line)
    {
        Lines.Add(line);
        if (line.Words.Count <= 0)
        {
            return;
        }

        var lastWord = line.Words[^1];
        EstimatedEndTime = Math.Max(
            EstimatedEndTime,
            lastWord.AbsoluteStartTime + lastWord.Duration
        );
    }
}
