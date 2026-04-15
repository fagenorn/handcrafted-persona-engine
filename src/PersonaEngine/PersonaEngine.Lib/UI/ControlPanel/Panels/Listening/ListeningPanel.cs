using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Listening.Sections;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Listening;

/// <summary>
///     Orchestrator for the Listening panel. Stacks four sections vertically inside a
///     single scrollable container. Owns nothing beyond its child sections.
/// </summary>
public sealed class ListeningPanel(
    Sections.MicrophoneDeviceSection deviceSection,
    Sections.SpeechDetectionSection detectionSection,
    Sections.RecognitionSection recognitionSection,
    Sections.InterruptionSection interruptionSection
)
{
    public void Render(float dt)
    {
        using (Ui.FillChild("##listening_panel", padding: 4f))
        {
            deviceSection.Render(dt);
            ImGui.Spacing();
            detectionSection.Render(dt);
            ImGui.Spacing();
            recognitionSection.Render(dt);
            ImGui.Spacing();
            interruptionSection.Render(dt);
        }
    }
}
