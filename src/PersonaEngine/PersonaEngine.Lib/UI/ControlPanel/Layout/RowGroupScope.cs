using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Pre-resolves a sequence of row sizes using <see cref="Sz.Resolve"/> so that
///     Fill rows correctly account for all sibling Fixed rows.
///     Call <see cref="Next"/> for each row in order.
/// </summary>
public ref struct RowGroupScope
{
    private readonly float[] _resolvedHeights;
    private readonly float _parentWidth;
    private readonly Vector2 _parentSpacing;
    private int _nextIndex;
    private bool _disposed;

    internal RowGroupScope(ReadOnlySpan<Sz> sizes)
    {
        var available = LayoutContext.RemainingHeight();
        _parentWidth = LayoutContext.Width();
        _resolvedHeights = Sz.Resolve(sizes, available);
        _parentSpacing = ImGui.GetStyle().ItemSpacing;
        _nextIndex = 0;
        _disposed = false;

        // Reserve all available height so no sibling after this group can claim it.
        LayoutContext.ConsumeHeight(available);
    }

    /// <summary>
    ///     Opens the next row with the pre-resolved height.
    ///     Must be called exactly once per <see cref="Sz"/> passed to the constructor, in order.
    /// </summary>
    public RowItemScope Next(
        Style style = default,
        ImGuiChildFlags childFlags = ImGuiChildFlags.None
    )
    {
        var height =
            _nextIndex < _resolvedHeights.Length ? MathF.Max(0f, _resolvedHeights[_nextIndex]) : 0f;
        _nextIndex++;
        return new RowItemScope(_parentWidth, height, style, childFlags, _parentSpacing);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
    }
}

/// <summary>
///     A single row within a <see cref="RowGroupScope"/>.
///     Handles the two-phase style push: container (padding, bg) before BeginChild,
///     content (item spacing) after BeginChild.
/// </summary>
public ref struct RowItemScope
{
    private int _containerVarCount;
    private int _containerColorCount;
    private bool _disposed;

    internal RowItemScope(
        float width,
        float height,
        Style style,
        ImGuiChildFlags childFlags,
        Vector2 parentSpacing
    )
    {
        _disposed = false;
        _containerVarCount = 0;
        _containerColorCount = 0;

        // Zero spacing on parent so rows tile edge-to-edge.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        _containerVarCount++;

        // Phase 1: Container style before BeginChild.
        var padding = Vector2.Zero;

        if (!style.IsDefault)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, style.Padding);
            _containerVarCount++;
            padding = style.Padding;

            if (style.ChildBg is { } bg)
            {
                ImGui.PushStyleColor(ImGuiCol.ChildBg, bg);
                _containerColorCount++;
            }
        }

        ImGui.BeginChild(
            $"##RowItem_{height:F0}_{ImGui.GetCursorPosY():F0}",
            new Vector2(width, height),
            childFlags
        );

        // Phase 2: Content style after BeginChild.
        // Default → inherit parent's spacing.  Explicit → use style's spacing.
        var innerSpacing = style.IsDefault ? parentSpacing : style.ItemSpacing;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, innerSpacing);

        var innerWidth = MathF.Max(0f, width - padding.X * 2f);
        var innerHeight = MathF.Max(0f, height - padding.Y * 2f);
        LayoutContext.Push(innerWidth, innerHeight);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        LayoutContext.Pop();
        ImGui.PopStyleVar(); // inner ItemSpacing
        ImGui.EndChild();
        if (_containerColorCount > 0)
            ImGui.PopStyleColor(_containerColorCount);
        ImGui.PopStyleVar(_containerVarCount); // zero IS + optional WP
    }
}
