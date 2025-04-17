namespace PersonaEngine.Lib.Configuration;

/// <summary>
///     Configuration for the subtitle renderer
/// </summary>
public class SubtitleOptions
{
    public string Font { get; set; } = "DynaPuff_Condensed-Bold.ttf";

    public int FontSize { get; set; } = 72;

    public string Color { get; set; } = "#FFFFFF";

    public string HighlightColor { get; set; } = "#4FC3F7";

    public int BottomMargin { get; set; } = 50;

    public int SideMargin { get; set; } = 100;

    public float InterSegmentSpacing { get; set; } = 16f;

    public int MaxVisibleLines { get; set; } = 6;

    public float AnimationDuration { get; set; } = 0.3f;
    
    public int StrokeThickness { get; set; } = 3;

    public int Width { get; set; } = 1920;

    public int Height { get; set; } = 1080;
}