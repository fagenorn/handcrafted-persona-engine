namespace PersonaEngine.Lib.Configuration;

public class WindowConfiguration
{
    public int Width { get; set; } = 1920;

    public int Height { get; set; } = 1080;

    public int MinWidth { get; set; } = 800;

    public int MinHeight { get; set; } = 600;

    public string Title { get; set; } = "Avatar Application";

    public bool Fullscreen { get; set; } = false;
}
