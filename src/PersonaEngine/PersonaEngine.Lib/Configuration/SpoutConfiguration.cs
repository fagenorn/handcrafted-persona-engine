namespace PersonaEngine.Lib.Configuration;

public record SpoutConfiguration
{
    public required string OutputName { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }
}
