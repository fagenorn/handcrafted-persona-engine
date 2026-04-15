namespace PersonaEngine.Lib.Configuration;

/// <summary>
///     Configuration for the floating, borderless, always-on-top overlay window that
///     displays the Live2D avatar and subtitles without any window chrome. The overlay
///     reads from the same in-process frame source that the Spout sender publishes, so
///     it adds essentially zero GPU cost beyond a fullscreen quad and a present.
/// </summary>
public record OverlayConfiguration
{
    /// <summary>
    ///     Whether the overlay window is created at startup. Toggling this at runtime
    ///     via the control panel shows or hides the overlay without restarting the app.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    ///     Name of the <see cref="SpoutConfiguration" /> to mirror. Must match an entry
    ///     in <see cref="AvatarAppConfig.SpoutConfigs" /> — today this is "Live2D" which
    ///     contains the avatar plus subtitles.
    /// </summary>
    public string Source { get; set; } = "Live2D";

    /// <summary>
    ///     Top-left corner of the overlay window in virtual screen coordinates.
    ///     Persisted when the user finishes moving the window.
    /// </summary>
    public int X { get; set; } = 100;

    public int Y { get; set; } = 100;

    /// <summary>
    ///     Size of the overlay window in pixels. Aspect is locked to the source's
    ///     native dimensions when <see cref="LockAspect" /> is true (default).
    /// </summary>
    public int Width { get; set; } = 360;

    public int Height { get; set; } = 640;

    /// <summary>
    ///     Minimum overlay size. Prevents the user from collapsing it to nothing.
    /// </summary>
    public int MinWidth { get; set; } = 120;

    public int MinHeight { get; set; } = 120;
}
