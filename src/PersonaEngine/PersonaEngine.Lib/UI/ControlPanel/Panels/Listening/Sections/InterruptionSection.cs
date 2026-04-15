using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Listening.Sections;

public sealed class InterruptionSection
{
    public void Render(float dt)
    {
        using (Ui.Card("##interruption", padding: 12f))
        {
            ImGui.TextUnformatted("Interruption (stub)");
        }
    }
}
