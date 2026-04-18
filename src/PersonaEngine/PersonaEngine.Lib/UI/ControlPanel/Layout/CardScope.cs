using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     A bordered, padded child-window scope used to visually group a "card" of content.
///     Follows the two-phase padding pattern of <see cref="FillChildScope"/>/<see cref="RowScope"/>:
///     WindowPadding is pushed before BeginChild for boundary calc, then left/top
///     padding is applied manually via Indent/Dummy, because WindowPadding is not
///     reliably applied to child windows in this ImGui binding.
/// </summary>
public ref struct CardScope
{
    private float _indentX;
    private bool _disposed;

    /// <summary>
    ///     The card's rendered height in pixels, available after <see cref="Dispose" />.
    ///     Used by <see cref="UniformHeightTracker" /> to enforce uniform sizing.
    /// </summary>
    public float RenderedHeight { get; private set; }

    internal CardScope(string id, float padding, bool border, float width = 0f, float height = 0f)
    {
        _disposed = false;
        _indentX = 0f;
        RenderedHeight = 0f;

        // Push padding so ImGui computes the right/bottom content boundary.
        // Left/top is applied manually after BeginChild.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padding, padding));

        // height = 0 → AutoResizeY (grow to fit content).
        // height > 0 → fixed height (used by UniformHeightTracker for equal-sized cards).
        var useAutoHeight = height <= 0f;
        var flags = border ? ImGuiChildFlags.Borders : ImGuiChildFlags.None;
        if (useAutoHeight)
            flags |= ImGuiChildFlags.AutoResizeY;
        ImGui.BeginChild(
            id,
            new Vector2(width, useAutoHeight ? 0f : height),
            flags,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
        );

        // Keep inner WP at zero so nested children don't inherit our padding.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        ImGui.PopStyleVar(); // inner WP reset

        if (_indentX > 0f)
            ImGui.Unindent(_indentX);

        ImGui.EndChild();
        // Capture the child window's actual rendered height (available after EndChild).
        RenderedHeight = ImGui.GetItemRectSize().Y;
        ImGui.PopStyleVar(); // outer WP
    }
}
