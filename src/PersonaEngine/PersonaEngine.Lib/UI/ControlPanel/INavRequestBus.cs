using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
///     In-process pub/sub for "switch the active panel to section X" requests.
///     Published by dashboard health cards and linked hints in other panels;
///     subscribed by <see cref="ControlPanelComponent" />.
/// </summary>
public interface INavRequestBus
{
    void Request(NavSection section);

    event Action<NavSection>? Requested;
}
