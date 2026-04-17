using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel;

public sealed class NavRequestBus : INavRequestBus
{
    public event Action<NavSection>? Requested;

    public void Request(NavSection section) => Requested?.Invoke(section);
}
