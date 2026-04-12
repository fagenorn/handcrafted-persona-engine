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

    /// <summary>Peeks the current layout context's available space.</summary>
    public static (float Width, float Height) PeekContext() => LayoutContext.Peek();
}
