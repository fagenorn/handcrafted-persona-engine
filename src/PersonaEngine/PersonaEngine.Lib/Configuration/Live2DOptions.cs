namespace PersonaEngine.Lib.Configuration;

public record Live2DOptions
{
    public string ModelPath { get; set; } = "Resources/live2d";

    public string ModelName { get; set; } = "angel";

    public int Width { get; set; } = 1920;

    public int Height { get; set; } = 1080;
}
