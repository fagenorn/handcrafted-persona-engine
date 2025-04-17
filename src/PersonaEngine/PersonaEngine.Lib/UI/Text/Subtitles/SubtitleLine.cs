namespace PersonaEngine.Lib.UI.Text.Subtitles;

/// <summary>
///     Represents a single line of processed subtitles containing multiple words.
/// </summary>
public class SubtitleLine
{
    public SubtitleLine(int segmentIndex, int lineIndexInSegment)
    {
        SegmentIndex       = segmentIndex;
        LineIndexInSegment = lineIndexInSegment;
    }

    public List<SubtitleWordInfo> Words { get; } = new();

    public float TotalWidth { get; set; }

    public float BaselineY { get; set; }

    public int SegmentIndex { get; }

    public int LineIndexInSegment { get; }

    public void AddWord(SubtitleWordInfo word)
    {
        Words.Add(word);
        TotalWidth += word.Size.X;
    }

    public void Clear()
    {
        Words.Clear();
        TotalWidth = 0;
        BaselineY  = 0;
    }
}