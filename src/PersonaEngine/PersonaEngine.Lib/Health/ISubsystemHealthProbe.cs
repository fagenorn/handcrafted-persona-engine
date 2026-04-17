using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.Health;

/// <summary>
///     A subsystem's own read-only health surface. Probes own their current
///     state and publish <see cref="StatusChanged" /> only on transitions.
/// </summary>
public interface ISubsystemHealthProbe
{
    string Name { get; }

    NavSection TargetPanel { get; }

    SubsystemStatus Current { get; }

    event Action<SubsystemStatus>? StatusChanged;
}
