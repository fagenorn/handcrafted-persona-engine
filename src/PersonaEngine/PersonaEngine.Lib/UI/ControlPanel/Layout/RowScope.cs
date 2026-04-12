using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Horizontal band spanning the full parent width.
///     Height is determined by <see cref="Sz"/>: fixed pixels or remaining space.
///     Uses two-phase style push: container style (padding, bg) before BeginChild,
///     content style (item spacing) after BeginChild.  When no style is specified,
///     the child inherits the parent's item spacing instead of zeroing it.
/// </summary>
public ref struct RowScope
{
    private int _containerVarCount;
    private int _containerColorCount;
    private bool _disposed;

    internal RowScope(Sz height, Style style, ImGuiChildFlags childFlags)
    {
        _disposed = false;
        _containerVarCount = 0;
        _containerColorCount = 0;

        var parentWidth = LayoutContext.Width();
        float resolvedHeight;

        if (height.IsFixed)
        {
            resolvedHeight = height.Value;
        }
        else
        {
            resolvedHeight = LayoutContext.RemainingHeight();
        }

        resolvedHeight = MathF.Max(0f, resolvedHeight);
        LayoutContext.ConsumeHeight(resolvedHeight);

        // Capture the parent's item spacing BEFORE we zero it for tiling.
        // We'll restore this inside the child if no explicit style is given.
        var parentSpacing = ImGui.GetStyle().ItemSpacing;

        // Zero spacing on parent so consecutive rows tile edge-to-edge.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        _containerVarCount++;

        // ── Phase 1: Container style (before BeginChild) ────────────────
        // WindowPadding and ChildBg must be set before BeginChild so the
        // child window captures them for its content region.
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
            $"##Row_{resolvedHeight:F0}_{ImGui.GetCursorPosY():F0}",
            new Vector2(parentWidth, resolvedHeight),
            childFlags
        );

        // ── Phase 2: Content style (after BeginChild) ───────────────────
        // ItemSpacing is set INSIDE the child so content lays out properly.
        // Default style → inherit parent's spacing (transparent behavior).
        // Explicit style → use the style's specified spacing.
        var innerSpacing = style.IsDefault ? parentSpacing : style.ItemSpacing;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, innerSpacing);

        var innerWidth = MathF.Max(0f, parentWidth - padding.X * 2f);
        var innerHeight = MathF.Max(0f, resolvedHeight - padding.Y * 2f);
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
        ImGui.PopStyleVar(_containerVarCount); // parent zero IS + optional WP
    }
}
