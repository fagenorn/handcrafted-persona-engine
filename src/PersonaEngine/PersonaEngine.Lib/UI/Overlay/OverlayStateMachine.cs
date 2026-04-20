using Stateless;

namespace PersonaEngine.Lib.UI.Overlay;

/// <summary>
///     Stateless-based lifecycle machine for the floating overlay. Entry actions are
///     supplied as constructor callbacks so the transition topology is testable
///     without Win32, D3D11, or the <see cref="OverlayHost" />.
///
///     Transition table:
///     <code>
///       Off      ──TurnOn──>       Starting   (onStartThread)
///       Starting ──Ready──>        Active
///       Starting ──TurnOff──>      Stopping   (onPostClose)
///       Starting ──Failed──>       Failed
///       Starting ──ThreadExited──> Off         (external close)
///       Active   ──TurnOff──>      Stopping   (onPostClose)
///       Active   ──Failed──>       Failed
///       Active   ──ThreadExited──> Off         (external close)
///       Stopping ──ThreadExited──> Off         (and Off.OnEntry reconciles
///                                               by firing TurnOn if desired)
///       Stopping ──Failed──>       Failed
///       Failed   ──TurnOn──>       Starting   (retry, onStartThread)
///       Failed   ──TurnOff──>      Off
///     </code>
///
///     The "reverse intent mid-Stopping" case: TurnOn during Stopping is ignored,
///     but once ThreadExited fires and we settle to Off, <c>Off.OnEntry</c> reads
///     <c>getDesiredEnabled</c> and fires TurnOn to respawn.
/// </summary>
public sealed class OverlayStateMachine
{
    private readonly StateMachine<OverlayStatus, OverlayTrigger> _machine;

    public OverlayStateMachine(
        Action onStartThread,
        Action onPostClose,
        Func<bool> getDesiredEnabled
    )
    {
        _machine = new StateMachine<OverlayStatus, OverlayTrigger>(OverlayStatus.Off);

        _machine
            .Configure(OverlayStatus.Off)
            .OnEntry(
                () =>
                {
                    // Reconcile: if the user flipped intent back on while we were
                    // Stopping, respawn now that the old thread has cleared.
                    if (getDesiredEnabled())
                    {
                        _machine.Fire(OverlayTrigger.TurnOn);
                    }
                },
                "Reconcile desired on settle"
            )
            .Permit(OverlayTrigger.TurnOn, OverlayStatus.Starting)
            .Ignore(OverlayTrigger.TurnOff)
            .Ignore(OverlayTrigger.ThreadExited)
            .Ignore(OverlayTrigger.Ready)
            .Ignore(OverlayTrigger.Failed);

        _machine
            .Configure(OverlayStatus.Starting)
            .OnEntry(onStartThread, "Spawn overlay thread")
            .Permit(OverlayTrigger.Ready, OverlayStatus.Active)
            .Permit(OverlayTrigger.TurnOff, OverlayStatus.Stopping)
            .Permit(OverlayTrigger.Failed, OverlayStatus.Failed)
            .Permit(OverlayTrigger.ThreadExited, OverlayStatus.Off)
            .Ignore(OverlayTrigger.TurnOn);

        _machine
            .Configure(OverlayStatus.Active)
            .Permit(OverlayTrigger.TurnOff, OverlayStatus.Stopping)
            .Permit(OverlayTrigger.Failed, OverlayStatus.Failed)
            .Permit(OverlayTrigger.ThreadExited, OverlayStatus.Off)
            .Ignore(OverlayTrigger.TurnOn)
            .Ignore(OverlayTrigger.Ready);

        _machine
            .Configure(OverlayStatus.Stopping)
            .OnEntry(onPostClose, "Post WM_CLOSE to overlay thread")
            .Permit(OverlayTrigger.ThreadExited, OverlayStatus.Off)
            .Permit(OverlayTrigger.Failed, OverlayStatus.Failed)
            .Ignore(OverlayTrigger.TurnOn)
            .Ignore(OverlayTrigger.TurnOff)
            .Ignore(OverlayTrigger.Ready);

        _machine
            .Configure(OverlayStatus.Failed)
            .Permit(OverlayTrigger.TurnOn, OverlayStatus.Starting)
            .Permit(OverlayTrigger.TurnOff, OverlayStatus.Off)
            .Ignore(OverlayTrigger.ThreadExited)
            .Ignore(OverlayTrigger.Ready)
            .Ignore(OverlayTrigger.Failed);
    }

    /// <summary>Current state.</summary>
    public OverlayStatus State => _machine.State;

    /// <summary>Fires a trigger. Safe to call from entry actions (Stateless handles reentrancy).</summary>
    public void Fire(OverlayTrigger trigger) => _machine.Fire(trigger);

    /// <summary>Returns true if the given trigger would cause a transition or matched <c>Ignore</c>.</summary>
    public bool CanFire(OverlayTrigger trigger) => _machine.CanFire(trigger);
}
