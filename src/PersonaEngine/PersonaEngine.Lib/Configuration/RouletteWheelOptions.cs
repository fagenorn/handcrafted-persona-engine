namespace PersonaEngine.Lib.Configuration;

public record RouletteWheelOptions
{
    // Font and Text Settings
    public string Font { get; set; } = "DynaPuff_Condensed-Bold.ttf";

    public int FontSize { get; set; } = 24;

    public string TextColor { get; set; } = "#FFFFFF";

    public float TextScale { get; set; } = 1f;

    public int TextStroke { get; set; } = 2;

    public bool AdaptiveText { get; set; } = true;

    public bool RadialTextOrientation { get; set; } = true;

    // Wheel Configuration
    public string[] SectionLabels { get; set; } = [];

    public float SpinDuration { get; set; } = 8f;

    public float MinRotations { get; set; } = 5f;

    public float WheelSizePercentage { get; set; } = 1.0f;

    // Viewport and Position
    public int Width { get; set; } = 1080;

    public int Height { get; set; } = 1080;

    public string PositionMode { get; set; } = "Anchored"; // "Absolute", "Percentage", or "Anchored"

    public string ViewportAnchor { get; set; } = "Center"; // One of the ViewportAnchor enum values

    public float PositionXPercentage { get; set; } = 0.5f;

    public float PositionYPercentage { get; set; } = 0.5f;

    public float AnchorOffsetX { get; set; } = 0f;

    public float AnchorOffsetY { get; set; } = 0f;

    public float AbsolutePositionX { get; set; } = 0f;

    public float AbsolutePositionY { get; set; } = 0f;

    // State and Animation
    public bool Enabled { get; set; } = false;

    public float RotationDegrees { get; set; } = 0;

    public bool AnimateToggle { get; set; } = true;

    public float AnimationDuration { get; set; } = 0.5f;
}
