using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Sections;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard;

/// <summary>
///     Thin orchestrator for the Dashboard panel. Every section except the
///     transcript sizes to its own content via <see cref="Sz.Auto"/>; the
///     transcript claims whatever space remains via <see cref="Sz.Fill"/>.
///     The health strip and transcript share a nested gap-0 row group so they
///     still read as one conversation surface the chat is flowing across.
/// </summary>
public sealed class Dashboard(
    PresenceStripSection presenceStrip,
    SystemHealthSection systemHealth,
    TranscriptSection transcript,
    ControlsSection controls,
    SessionStatsSection sessionStats
) : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // PresenceStripSection, TranscriptSection, and SessionStatsSection are not
        // IDisposable — nothing to tear down. ControlsSection hooks orchestrator /
        // mute-controller events, SystemHealthSection hooks probe.StatusChanged —
        // both must unsubscribe to avoid handler leaks after shutdown.
        controls.Dispose();
        systemHealth.Dispose();
    }

    public void Render(float deltaTime)
    {
        using var rows = Ui.Rows(
            "Dashboard.outer",
            12f,
            Sz.Auto(), // presence strip — natural height
            Sz.Fill(), // health + transcript rendered as a seamless pair
            Sz.Auto(), // controls — natural height
            Sz.Auto() // session stats — natural height
        );

        using (rows.Next())
            presenceStrip.Render(deltaTime);

        using (rows.Next())
        {
            // Nested gap=0 group: the health strip tiles directly into the
            // transcript below it so they read as one conversation surface.
            using var inner = Ui.Rows(
                "Dashboard.conversation",
                12f,
                Sz.Auto(), // health strip
                Sz.Fill() // transcript
            );

            using (inner.Next())
                systemHealth.Render(deltaTime);

            using (inner.Next())
                transcript.Render(deltaTime);
        }

        using (rows.Next())
            controls.Render(deltaTime);

        using (rows.Next())
            sessionStats.Render(deltaTime);
    }
}
