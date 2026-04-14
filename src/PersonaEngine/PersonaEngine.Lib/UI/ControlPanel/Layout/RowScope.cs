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
    private int _outerVarCount;
    private int _outerColorCount;
    private float _indentX;
    private bool _disposed;

    internal RowScope(Sz height, Style style, ImGuiChildFlags childFlags)
    {
        _disposed = false;
        _outerVarCount = 0;
        _outerColorCount = 0;
        _indentX = 0f;

        var parentWidth = LayoutContext.Width();
        float resolvedHeight = height.IsFixed ? height.Value : LayoutContext.RemainingHeight();
        resolvedHeight = MathF.Max(0f, resolvedHeight);
        LayoutContext.ConsumeHeight(resolvedHeight);

        var parentSpacing = ImGui.GetStyle().ItemSpacing;
        var padding = style.IsDefault ? Vector2.Zero : style.Padding;
        var innerSpacing = style.IsDefault ? parentSpacing : style.ItemSpacing;

        // ── Before BeginChild ───────────────────────────────────────────
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        _outerVarCount++;

        // Push padding so ImGui computes the right/bottom content boundary.
        // Left/top is applied manually after BeginChild (see below).
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
        _outerVarCount++;

        // Shell rows should never have rounded corners — they are full-width
        // horizontal bands. Push zero rounding for this child window only;
        // nested children (cards, panels) manage their own rounding.
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0f);
        _outerVarCount++;

        if (style.ChildBg is { } bg)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, bg);
            _outerColorCount++;
        }

        ImGui.BeginChild(
            $"##Row_{resolvedHeight:F0}_{ImGui.GetCursorPosY():F0}",
            new Vector2(parentWidth, resolvedHeight),
            childFlags
        );

        // ── After BeginChild ────────────────────────────────────────────
        // Apply left/top padding manually via Indent/Dummy — the cursor
        // start position is not reliably offset by WindowPadding.
        if (padding.Y > 0f)
            ImGui.Dummy(new Vector2(0f, padding.Y));

        if (padding.X > 0f)
        {
            ImGui.Indent(padding.X);
            _indentX = padding.X;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, innerSpacing);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

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
        ImGui.PopStyleVar(2); // inner WP reset + inner IS

        if (_indentX > 0f)
            ImGui.Unindent(_indentX);

        ImGui.EndChild();
        if (_outerColorCount > 0)
            ImGui.PopStyleColor(_outerColorCount);
        ImGui.PopStyleVar(_outerVarCount);
    }
}
