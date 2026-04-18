namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
///     Opt-in lifecycle hooks for panels that need to react when they become the active
///     navigation section (e.g., to acquire a resource scope) or when they cease to be
///     active (e.g., to release that scope). Implementations are invoked on the UI render
///     thread by <see cref="ControlPanelComponent" />.
/// </summary>
public interface IActivatablePanel
{
    /// <summary>
    ///     Called when the panel transitions from inactive to active. Called exactly once
    ///     per activation; the paired <see cref="OnDeactivated" /> is guaranteed to fire
    ///     before the next <see cref="OnActivated" />.
    /// </summary>
    void OnActivated();

    /// <summary>
    ///     Called when the panel transitions from active to inactive. Also called on
    ///     control-panel shutdown if this panel is the active one at that time.
    /// </summary>
    void OnDeactivated();
}
