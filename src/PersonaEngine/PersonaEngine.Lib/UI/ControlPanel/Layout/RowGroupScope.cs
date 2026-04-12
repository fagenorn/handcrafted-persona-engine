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
    private readonly float _gap;
    private readonly Vector2 _parentSpacing;
    private int _nextIndex;
    private bool _disposed;

    internal RowGroupScope(ReadOnlySpan<Sz> sizes, float gap)
    {
        _gap = gap;
        var totalGap = gap * MathF.Max(0f, sizes.Length - 1);
        var available = LayoutContext.RemainingHeight() - totalGap;
        _parentWidth = LayoutContext.Width();
        _resolvedHeights = Sz.Resolve(sizes, available);
        _parentSpacing = ImGui.GetStyle().ItemSpacing;
        _nextIndex = 0;
        _disposed = false;

        // Reserve all available height so no sibling after this group can claim it.
        LayoutContext.ConsumeHeight(available + totalGap);

        // Zero out item spacing so rows + gap dummies tile exactly.
        // Restored in Dispose.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
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
        // Emit gap spacing before every row except the first
        if (_nextIndex > 0 && _gap > 0f)
            ImGui.Dummy(new Vector2(0f, _gap));

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

        ImGui.PopStyleVar(); // ItemSpacing zero
    }
}

/// <summary>
///     A single row within a <see cref="RowGroupScope"/>.
///     Handles the two-phase style push: container (padding, bg) before BeginChild,
///     content (item spacing) after BeginChild.
/// </summary>
public ref struct RowItemScope
{
    private int _outerVarCount;
    private int _outerColorCount;
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
        _outerVarCount = 0;
        _outerColorCount = 0;

        var padding = style.IsDefault ? Vector2.Zero : style.Padding;
        var innerSpacing = style.IsDefault ? parentSpacing : style.ItemSpacing;

        // ── Before BeginChild ───────────────────────────────────────────
        // Zero IS so rows tile edge-to-edge.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        _outerVarCount++;

        // ALWAYS set WP explicitly — never let the parent's WP leak in.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
        _outerVarCount++;

        if (style.ChildBg is { } bg)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, bg);
            _outerColorCount++;
        }

        ImGui.BeginChild(
            $"##RowItem_{height:F0}_{ImGui.GetCursorPosY():F0}",
            new Vector2(width, height),
            childFlags
        );

        // ── After BeginChild ────────────────────────────────────────────
        // Set content spacing for items inside this row.
        // Reset WP to zero so nested BeginChild calls don't inherit our padding.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, innerSpacing);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

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
        ImGui.PopStyleVar(2); // inner WP reset + inner IS
        ImGui.EndChild();
        if (_outerColorCount > 0)
            ImGui.PopStyleColor(_outerColorCount);
        ImGui.PopStyleVar(_outerVarCount); // outer IS(0) + outer WP
    }
}
