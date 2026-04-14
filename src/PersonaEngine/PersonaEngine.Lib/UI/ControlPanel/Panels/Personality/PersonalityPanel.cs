using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Personality.Sections;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Personality;

/// <summary>
///     Orchestrator for the Personality panel. Owns nothing beyond its child sections.
///     All sections stack vertically inside a single scrollable container.
/// </summary>
public sealed class PersonalityPanel(
    PromptSourceSection promptSource,
    CurrentVibeSection currentVibe,
    TopicsSection topics
)
{
    public void Render(float dt)
    {
        using (Ui.FillChild("##personality_panel", padding: 4f))
        {
            promptSource.Render(dt);
            ImGui.Spacing();
            currentVibe.Render(dt);
            ImGui.Spacing();
            topics.Render(dt);
        }
    }
}
