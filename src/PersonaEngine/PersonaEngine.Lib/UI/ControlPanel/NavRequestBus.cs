using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
///     Default in-process implementation of <see cref="INavRequestBus" />. Publish
///     and subscribe are expected to occur on the ImGui render thread — there is
///     no cross-thread synchronization.
/// </summary>
public sealed class NavRequestBus : INavRequestBus
{
    public event Action<NavSection>? Requested;

    public void Request(NavSection section) => Requested?.Invoke(section);
}
