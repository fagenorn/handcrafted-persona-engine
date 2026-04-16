using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Avatar.Sections;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Avatar;

/// <summary>
///     Orchestrator for the Avatar panel. Owns nothing beyond its child sections.
///     Sections stack vertically inside a single scrollable container.
/// </summary>
public sealed class AvatarPanel(ModelSection model, LipSyncSection lipSync)
{
    public void Render(float dt)
    {
        using (Ui.FillChild("##avatar_panel", padding: 4f))
        {
            model.Render(dt);
            ImGui.Spacing();
            lipSync.Render(dt);
        }
    }
}
