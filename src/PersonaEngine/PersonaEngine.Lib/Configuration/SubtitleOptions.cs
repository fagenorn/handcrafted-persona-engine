namespace PersonaEngine.Lib.Configuration;

/// <summary>
///     Configuration for the subtitle renderer.
/// </summary>
public record SubtitleOptions
{
    public string Font { get; set; } = "DynaPuff.ttf";

    public int FontSize { get; set; } = 125;

    public string Color { get; set; } = "#FFf8f6f7";

    public string HighlightColor { get; set; } = "#FFc4251e";

    public int BottomMargin { get; set; } = 250;

    public int SideMargin { get; set; } = 30;

    public float InterSegmentSpacing { get; set; } = 10f;

    public int MaxVisibleLines { get; set; } = 2;

    public float AnimationDuration { get; set; } = 0.3f;

    public int StrokeThickness { get; set; } = 3;

    public int Width { get; set; } = 1080;

    public int Height { get; set; } = 1920;
}
