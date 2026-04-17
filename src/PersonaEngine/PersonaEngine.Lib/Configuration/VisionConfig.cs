namespace PersonaEngine.Lib.Configuration;

public record VisionConfig
{
    public string WindowTitle { get; init; } = "Paint";

    public TimeSpan CaptureInterval { get; init; } = TimeSpan.FromSeconds(45);

    public int CaptureMinPixels { get; init; } = 224 * 224;

    public int CaptureMaxPixels { get; init; } = 2048 * 2048;
}
