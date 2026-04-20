using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.Health;

/// <summary>
///     A subsystem's own read-only health surface. Probes own their current
///     state and publish <see cref="StatusChanged" /> only on transitions.
/// </summary>
/// <remarks>
///     <see cref="StatusChanged" /> fires on transitions only. A consumer that subscribes
///     late will not receive a synthetic "current state" event — it must read
///     <see cref="Current" /> once immediately after subscribing to bootstrap its view.
/// </remarks>
public interface ISubsystemHealthProbe
{
    string Name { get; }

    NavSection TargetPanel { get; }

    SubsystemStatus Current { get; }

    event Action<SubsystemStatus>? StatusChanged;
}
