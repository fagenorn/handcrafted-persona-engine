using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     A child window that fills all remaining space in the current context.
///     Useful for placing a scrollable/bordered area after a header or other content.
/// </summary>
public ref struct FillChildScope
{
    private float _indentX;
    private bool _disposed;

    internal FillChildScope(
        string id,
        Vector2 padding,
        ImGuiChildFlags childFlags,
        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.None
    )
    {
        _disposed = false;
        _indentX = 0f;

        // Push padding so ImGui computes the right/bottom content boundary.
        // Left/top is applied manually after BeginChild (see below).
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);

        ImGui.BeginChild(id, ImGui.GetContentRegionAvail(), childFlags, windowFlags);

        // Apply padding manually via Indent/Dummy — WindowPadding is not
        // reliably applied to child windows in this ImGui binding.
        if (padding.Y > 0f)
            ImGui.Dummy(new Vector2(0f, padding.Y));

        if (padding.X > 0f)
        {
            ImGui.Indent(padding.X);
            _indentX = padding.X;
        }

        // Keep WP zero so nested children don't inherit our padding.
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
        ImGui.PopStyleVar(); // outer WP
    }
}
