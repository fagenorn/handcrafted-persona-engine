using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     A child window that fills all remaining space in the current context.
///     Useful for placing a scrollable/bordered area after a header or other content.
/// </summary>
public ref struct FillChildScope
{
    private bool _disposed;

    internal FillChildScope(string id, Vector2 padding, ImGuiChildFlags childFlags)
    {
        _disposed = false;

        // Explicit WP before BeginChild — never inherit parent's.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);

        ImGui.BeginChild(id, ImGui.GetContentRegionAvail(), childFlags);

        // Reset WP inside so nested children don't inherit our padding.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        ImGui.PopStyleVar(); // inner WP reset
        ImGui.EndChild();
        ImGui.PopStyleVar(); // outer WP
    }
}
