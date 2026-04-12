namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Static entry points for the declarative layout system.
/// </summary>
public static class Ui
{
    /// <summary>Creates a fullscreen ImGui window scope.</summary>
    public static WindowScope Window(string id) => new(id);

    /// <summary>Creates a horizontal band scope with the given height and style.</summary>
    public static RowScope Row(Sz height, Style style = default) => new(height, style);

    /// <summary>Creates a resizable horizontal split scope.</summary>
    public static SplitScope HSplit(
        string id,
        Sz height,
        float initialLeft,
        float minLeft = 50f,
        float minRight = 100f,
        Style leftStyle = default,
        Style rightStyle = default
    ) => new(id, height, initialLeft, minLeft, minRight, leftStyle, rightStyle);

    /// <summary>Peeks the current layout context's available space.</summary>
    public static (float Width, float Height) PeekContext() => LayoutContext.Peek();
}
