using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Listening.Sections;

public sealed class MicrophoneDeviceSection
{
    public void Render(float dt)
    {
        using (Ui.Card("##mic_device", padding: 12f))
        {
            ImGui.TextUnformatted("Microphone (stub)");
        }
    }
}
