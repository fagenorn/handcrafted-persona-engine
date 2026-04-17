using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Sections;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard;

/// <summary>
///     Thin orchestrator for the Dashboard panel. Uses a four-row layout:
///     presence strip (fixed, overlay quick-toggle), health cards (fixed),
///     transcript (fill), session stats (fixed). Stats are pinned to the
///     bottom — the transcript fills remaining space.
/// </summary>
public sealed class Dashboard(
    PresenceStripSection presenceStrip,
    SystemHealthSection systemHealth,
    TranscriptSection transcript,
    SessionStatsSection sessionStats
)
{
    private const float HealthSectionHeight = 132f;
    private const float StatsSectionHeight = 100f;

    public void Render(float deltaTime)
    {
        using var rows = Ui.Rows(
            12f,
            Sz.Fixed(PresenceStripSection.StripHeight),
            Sz.Fixed(HealthSectionHeight),
            Sz.Fill(),
            Sz.Fixed(StatsSectionHeight)
        );

        using (rows.Next())
            presenceStrip.Render(deltaTime);

        using (rows.Next())
            systemHealth.Render(deltaTime);

        using (rows.Next())
            transcript.Render(deltaTime);

        using (rows.Next())
            sessionStats.Render(deltaTime);
    }
}
