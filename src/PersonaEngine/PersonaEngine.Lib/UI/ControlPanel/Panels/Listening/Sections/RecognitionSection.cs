using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Listening.Sections;

public sealed class RecognitionSection
{
    public void Render(float dt)
    {
        using (Ui.Card("##recognition", padding: 12f))
        {
            ImGui.TextUnformatted("Recognition (stub)");
        }
    }
}
