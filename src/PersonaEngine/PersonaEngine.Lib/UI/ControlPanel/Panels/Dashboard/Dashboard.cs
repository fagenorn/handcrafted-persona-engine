using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Sections;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard;

/// <summary>
///     Thin orchestrator for the Dashboard panel. Sections stack vertically
///     inside a single scrollable container.
/// </summary>
public sealed class Dashboard(
    SystemHealthSection systemHealth,
    TranscriptSection transcript,
    SessionStatsSection sessionStats
)
{
    /// <summary>
    ///     Height reserved for the session stats section (header + table + spacing).
    /// </summary>
    private const float StatsReservedHeight = 80f;

    public void Render(float deltaTime)
    {
        using (Ui.FillChild("##dashboard_panel", padding: 4f))
        {
            systemHealth.Render(deltaTime);
            ImGui.Spacing();

            // Give the transcript all available height minus what stats needs.
            var transcriptBudget = ImGui.GetContentRegionAvail().Y - StatsReservedHeight;
            transcript.Render(deltaTime, transcriptBudget);
            ImGui.Spacing();

            sessionStats.Render(deltaTime);
        }
    }
}
