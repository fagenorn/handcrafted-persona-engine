using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Personality.Sections;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Personality;

/// <summary>
///     Orchestrator for the Personality panel. Owns nothing beyond its child sections.
///     All sections stack vertically inside a single scrollable container.
///     <para>
///         All three child sections hold <c>IOptionsMonitor.OnChange</c> subscriptions,
///         so <see cref="Dispose" /> forwards teardown to them to avoid handler leaks
///         when the control panel is torn down.
///     </para>
/// </summary>
public sealed class PersonalityPanel(
    PromptSourceSection promptSource,
    CurrentVibeSection currentVibe,
    TopicsSection topics
) : IDisposable
{
    private bool _disposed;

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        promptSource.Dispose();
        currentVibe.Dispose();
        topics.Dispose();
    }
}
