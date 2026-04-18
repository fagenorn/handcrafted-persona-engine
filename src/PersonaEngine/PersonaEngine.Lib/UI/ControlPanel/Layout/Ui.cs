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
    ///     Does not support <see cref="Sz.Auto"/>; use the <c>id</c>-carrying overloads
    ///     for mixed Fixed/Auto/Fill groups.
    ///     <para>
    ///         Span overload — preferred to avoid heap allocation of variadic arguments.
    ///         C# 12 collection expressions (<c>[Sz.Fixed(40), Sz.Fill()]</c>) bind here
    ///         automatically.
    ///     </para>
    /// </summary>
    public static RowGroupScope Rows(ReadOnlySpan<Sz> sizes) => new(string.Empty, sizes, 0f);

    /// <summary>
    ///     Pre-resolves a sequence of row sizes with a gap between each row.
    ///     Span overload — see <see cref="Rows(ReadOnlySpan{Sz})"/>.
    /// </summary>
    public static RowGroupScope Rows(float gap, ReadOnlySpan<Sz> sizes) =>
        new(string.Empty, sizes, gap);

    /// <summary>
    ///     Identity-keyed row group that supports <see cref="Sz.Auto"/> alongside
    ///     <see cref="Sz.Fixed"/> and <see cref="Sz.Fill"/>. Auto-row heights are
    ///     measured each frame and cached per <paramref name="id"/> so Fill rows can
    ///     compute their share correctly without a second pass. Use distinct ids for
    ///     distinct call sites (sharing an id collapses their caches, which is only
    ///     safe if the two sites always render identically-shaped groups).
    ///     <para>
    ///         Ids MUST come from a finite, stable set — one stable string per call site.
    ///         The cache is bounded (drop-oldest at 256 entries) so a varying-id caller
    ///         cannot leak unboundedly, but varying ids also defeat the auto-height
    ///         cache (every frame starts from zeros).
    ///     </para>
    /// </summary>
    public static RowGroupScope Rows(string id, ReadOnlySpan<Sz> sizes) => new(id, sizes, 0f);

    /// <summary>
    ///     Identity-keyed row group (see <see cref="Rows(string, ReadOnlySpan{Sz})"/>) with
    ///     a gap between rows.
    /// </summary>
    public static RowGroupScope Rows(string id, float gap, ReadOnlySpan<Sz> sizes) =>
        new(id, sizes, gap);

    /// <summary>
    ///     Variadic overload — forwards to the span overload. Kept so existing call sites
    ///     compile unchanged; new call sites should prefer collection-expression / span
    ///     forms to avoid the params-array allocation.
    /// </summary>
    public static RowGroupScope Rows(params Sz[] sizes) => new(string.Empty, sizes, 0f);

    /// <summary>Variadic overload — see <see cref="Rows(float, ReadOnlySpan{Sz})"/>.</summary>
    public static RowGroupScope Rows(float gap, params Sz[] sizes) =>
        new(string.Empty, sizes, gap);

    /// <summary>Variadic overload — see <see cref="Rows(string, ReadOnlySpan{Sz})"/>.</summary>
    public static RowGroupScope Rows(string id, params Sz[] sizes) => new(id, sizes, 0f);

    /// <summary>Variadic overload — see <see cref="Rows(string, float, ReadOnlySpan{Sz})"/>.</summary>
    public static RowGroupScope Rows(string id, float gap, params Sz[] sizes) =>
        new(id, sizes, gap);

    /// <summary>
    ///     Creates N equal-width columns side by side. Call <see cref="EqualColsScope.NextCol"/>
    ///     for each column to render into it.
    /// </summary>
    public static EqualColsScope EqualCols(
        int count,
        float height,
        float gap = 12f,
        ImGuiChildFlags childFlags = ImGuiChildFlags.None,
        float padding = 0f
    ) => new(count, height, gap, childFlags, padding);

    /// <summary>
    ///     Creates a child window that fills all remaining space in the current context.
    ///     Useful after a header or other content to fill the rest of a row.
    /// </summary>
    public static FillChildScope FillChild(
        string id,
        ImGuiChildFlags childFlags = ImGuiChildFlags.None,
        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.None,
        float padding = 0f
    ) => new(id, new System.Numerics.Vector2(padding, padding), childFlags, windowFlags);

    /// <summary>
    ///     Creates a bordered, padded card scope. Auto-sizes vertically by default
    ///     (<c>ImGuiChildFlags.AutoResizeY</c>). Pass <paramref name="height" /> &gt; 0
    ///     for a fixed height (e.g., from <see cref="UniformHeightTracker" />).
    /// </summary>
    public static CardScope Card(
        string id,
        float padding = 12f,
        bool border = true,
        float width = 0f,
        float height = 0f
    ) => new(id, padding, border, width, height);

    /// <summary>
    ///     Creates an auto-height equal-width column grid. Unlike <see cref="EqualCols"/>,
    ///     rows auto-size to content — no fixed height, no child windows, no scrollbars.
    ///     Call <see cref="GridScope.Row"/> then <see cref="GridScope.Col"/> for each cell.
    /// </summary>
    public static GridScope Grid(string id, int columns) => new(id, columns);

    /// <summary>
    ///     Creates an auto-wrapping grid whose column count is derived from the available width
    ///     and the given tile size. Each tile is placed via <see cref="WrapGridScope.NextTile" />.
    /// </summary>
    public static WrapGridScope WrapGrid(
        string id,
        System.Numerics.Vector2 itemSize,
        float gap = 12f
    ) => new(id, itemSize, gap);

    /// <summary>Peeks the current layout context's available space.</summary>
    public static (float Width, float Height) PeekContext() => LayoutContext.Peek();
}
