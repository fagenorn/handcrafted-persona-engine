using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Sections;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard;

/// <summary>
///     Thin orchestrator for the Dashboard panel. Uses a five-row layout:
///     presence strip (fixed), health cards (fixed), transcript (fill),
///     controls (fixed), session stats (fixed).
/// </summary>
public sealed class Dashboard(
    PresenceStripSection presenceStrip,
    SystemHealthSection systemHealth,
    TranscriptSection transcript,
    ControlsSection controls,
    SessionStatsSection sessionStats
) : IDisposable
{
    private const float HealthSectionHeight = 132f;
    private const float ControlsSectionHeight = 64f;
    private const float StatsSectionHeight = 100f;

    public void Dispose()
    {
        controls.Dispose();
    }

    public void Render(float deltaTime)
    {
        using var rows = Ui.Rows(
            12f,
            Sz.Fixed(PresenceStripSection.StripHeight),
            Sz.Fixed(HealthSectionHeight),
            Sz.Fill(),
            Sz.Fixed(ControlsSectionHeight),
            Sz.Fixed(StatsSectionHeight)
        );

        using (rows.Next())
            presenceStrip.Render(deltaTime);

        using (rows.Next())
            systemHealth.Render(deltaTime);

        using (rows.Next())
            transcript.Render(deltaTime);

        using (rows.Next())
            controls.Render(deltaTime);

        using (rows.Next())
            sessionStats.Render(deltaTime);
    }
}
