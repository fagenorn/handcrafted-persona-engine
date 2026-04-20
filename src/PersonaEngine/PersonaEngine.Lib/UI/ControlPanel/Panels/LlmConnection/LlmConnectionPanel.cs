using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.LlmConnection.Sections;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.LlmConnection;

/// <summary>
///     Thin orchestrator for the LLM Connection panel. Text + Vision sections
///     stacked; each owns its own card, probe subscription, and config writes.
/// </summary>
public sealed class LlmConnectionPanel(TextLlmSection text, VisionLlmSection vision) : IDisposable
{
    public void Render(float dt)
    {
        using (Ui.FillChild("##llm_connection_panel", padding: 4f))
        {
            text.Render(dt);
            ImGui.Spacing();
            vision.Render(dt);
        }
    }

    public void Dispose()
    {
        text.Dispose();
        vision.Dispose();
    }
}
