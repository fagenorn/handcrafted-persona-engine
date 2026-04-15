namespace PersonaEngine.Lib.UI.Rendering;

/// <summary>
///     Publishes the GL color texture of a rendered frame for zero-copy consumption
///     by in-process components (e.g. the floating overlay). Implementations live in
///     the producer (today: <see cref="Spout.SpoutManager" />) and expose the color
///     attachment of a custom FBO. No extra rendering work is performed — the same
///     texture that feeds the Spout sender is reused as-is.
/// </summary>
public interface IFrameSource
{
    /// <summary>
    ///     GL texture handle of the color attachment. Zero if the source is not yet
    ///     initialized; consumers must handle this gracefully.
    /// </summary>
    uint ColorTextureHandle { get; }

    int Width { get; }

    int Height { get; }

    /// <summary>
    ///     True if the producer wrote the texture top-to-bottom in GL convention
    ///     (origin at bottom-left). Consumers that present with Y-down (swap chain,
    ///     screen) should flip the texture coordinates when this is true.
    /// </summary>
    bool OriginBottomLeft { get; }
}
