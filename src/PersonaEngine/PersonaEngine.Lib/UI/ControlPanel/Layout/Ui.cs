using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Static entry points for the declarative layout system.
/// </summary>
public static class Ui
{
    /// <summary>Creates a fullscreen ImGui window scope.</summary>
    public static WindowScope Window(string id) => new(id);

    /// <summary>Creates a horizontal band scope with the given height and style.</summary>
    public static RowScope Row(
        Sz height,
        Style style = default,
        ImGuiChildFlags childFlags = ImGuiChildFlags.None
    ) => new(height, style, childFlags);

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

    /// <summary>
    ///     Pre-resolves a sequence of row sizes so Fill rows correctly account for all
    ///     sibling Fixed rows. Call <see cref="RowGroupScope.Next"/> for each row in order.
    /// </summary>
    public static RowGroupScope Rows(params Sz[] sizes) => new(sizes);

    /// <summary>
    ///     Creates N equal-width columns side by side. Call <see cref="EqualColsScope.NextCol"/>
    ///     for each column to render into it.
    /// </summary>
    public static EqualColsScope EqualCols(
        int count,
        float height,
        float gap = 12f,
        ImGuiChildFlags childFlags = ImGuiChildFlags.None
    ) => new(count, height, gap, childFlags);

    /// <summary>
    ///     Creates a child window that fills all remaining space in the current context.
    ///     Useful after a header or other content to fill the rest of a row.
    /// </summary>
    public static FillChildScope FillChild(
        string id,
        ImGuiChildFlags childFlags = ImGuiChildFlags.None,
        float padding = 0f
    ) => new(id, new System.Numerics.Vector2(padding, padding), childFlags);

    /// <summary>Peeks the current layout context's available space.</summary>
    public static (float Width, float Height) PeekContext() => LayoutContext.Peek();
}
