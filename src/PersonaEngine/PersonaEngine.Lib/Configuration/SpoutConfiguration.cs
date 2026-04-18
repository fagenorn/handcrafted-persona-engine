namespace PersonaEngine.Lib.Configuration;

public record SpoutConfiguration
{
    public required string OutputName { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>
    ///     When false, the Spout sender is not created and no frames are published.
    ///     The in-process frame source (FBO) is still produced so that in-app consumers
    ///     such as the floating overlay continue to work without an OBS round-trip.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
