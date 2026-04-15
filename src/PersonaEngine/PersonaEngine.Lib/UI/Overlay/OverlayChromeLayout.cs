namespace PersonaEngine.Lib.UI.Overlay;

/// <summary>
///     The two interactive elements the floating overlay exposes on hover:
///     a drag button and a resize button, both clustered in the top-right
///     corner. Everything else is click-through.
/// </summary>
public enum OverlayHandle
{
    None,
    Drag,
    Resize,
}

/// <summary>
///     Geometry + cursor-to-handle classification for the overlay's hover chrome.
///     Pure math — no GPU state — shared between the chrome renderer (draws the
///     buttons) and the interaction controller (hit-tests cursor positions).
/// </summary>
public static class OverlayChromeLayout
{
    // Button geometry in overlay-local pixel space. Not DPI-scaled (same convention
    // as the rest of the overlay); if we ever wire PerMonitorV2, multiply by scale.
    public const int ButtonSize = 30;
    public const int ButtonCornerRadius = 7;
    public const int ButtonEdgePadding = 6;

    // Gap between the resize and drag buttons when sitting side-by-side in the
    // top-right cluster.
    public const int ButtonGap = 4;

    /// <summary>
    ///     Classifies a cursor position in window-relative pixels into the drag button,
    ///     the resize button, or none (anywhere else — click-through territory).
    /// </summary>
    public static OverlayHandle HitTest(int x, int y, int width, int height)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
        {
            return OverlayHandle.None;
        }

        var (dragX, dragY) = DragButtonPosition(width);
        if (IsInsideButton(x, y, dragX, dragY))
        {
            return OverlayHandle.Drag;
        }

        var (resizeX, resizeY) = ResizeButtonPosition(width, height);
        if (IsInsideButton(x, y, resizeX, resizeY))
        {
            return OverlayHandle.Resize;
        }

        return OverlayHandle.None;
    }

    /// <summary>Drag button — rightmost slot in the top cluster.</summary>
    public static (int X, int Y) DragButtonPosition(int width) =>
        (width - ButtonEdgePadding - ButtonSize, ButtonEdgePadding);

    /// <summary>
    ///     Resize button — sits immediately to the left of the drag button in the
    ///     top-right cluster. It still drives SE-corner resizes; co-locating both
    ///     chrome buttons keeps the interactive region compact so the avatar
    ///     isn't visually clipped by handles on opposite ends of the window.
    ///     The <paramref name="height" /> parameter is retained for API stability
    ///     (was previously needed when the resize anchored to the bottom corner).
    /// </summary>
    public static (int X, int Y) ResizeButtonPosition(int width, int height)
    {
        _ = height;
        return (width - ButtonEdgePadding - 2 * ButtonSize - ButtonGap, ButtonEdgePadding);
    }

    private static bool IsInsideButton(int x, int y, int buttonX, int buttonY) =>
        x >= buttonX && x < buttonX + ButtonSize && y >= buttonY && y < buttonY + ButtonSize;
}
