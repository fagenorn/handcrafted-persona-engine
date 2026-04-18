using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Avatar.Sections;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Avatar;

/// <summary>
///     Orchestrator for the Avatar panel. Owns nothing beyond its child sections.
///     Sections stack vertically inside a single scrollable container.
///     <para>
///         Both child sections hold <c>IOptionsMonitor.OnChange</c> subscriptions,
///         so <see cref="Dispose" /> forwards teardown to them to avoid handler
///         leaks when the control panel is torn down.
///     </para>
/// </summary>
public sealed class AvatarPanel(ModelSection model, LipSyncSection lipSync) : IDisposable
{
    private bool _disposed;

    public void Render(float dt)
    {
        using (Ui.FillChild("##avatar_panel", padding: 4f))
        {
            model.Render(dt);
            ImGui.Spacing();
            lipSync.Render(dt);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        model.Dispose();
        lipSync.Dispose();
    }
}
