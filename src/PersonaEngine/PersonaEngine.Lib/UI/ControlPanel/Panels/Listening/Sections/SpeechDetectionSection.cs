using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Listening.Sections;

public sealed class SpeechDetectionSection
{
    public void Render(float dt)
    {
        using (Ui.Card("##speech_detection", padding: 12f))
        {
            ImGui.TextUnformatted("Speech Detection (stub)");
        }
    }
}
