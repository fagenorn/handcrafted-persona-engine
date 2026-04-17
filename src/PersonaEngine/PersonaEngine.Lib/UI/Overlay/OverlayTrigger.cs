namespace PersonaEngine.Lib.UI.Overlay;

/// <summary>
///     Triggers that drive the <see cref="OverlayStateMachine" />.
/// </summary>
public enum OverlayTrigger
{
    /// <summary>User wants the overlay on. Fired from <c>SetEnabled(true)</c>.</summary>
    TurnOn,

    /// <summary>User wants the overlay off. Fired from <c>SetEnabled(false)</c>.</summary>
    TurnOff,

    /// <summary>Overlay thread is past init and pumping messages.</summary>
    Ready,

    /// <summary>Overlay thread has exited (host-requested or external close).</summary>
    ThreadExited,

    /// <summary>Overlay thread threw during startup or while running.</summary>
    Failed,
}
