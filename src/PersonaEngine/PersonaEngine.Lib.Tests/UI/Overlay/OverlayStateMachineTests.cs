using PersonaEngine.Lib.UI.Overlay;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.Overlay;

public class OverlayStateMachineTests
{
    private sealed class Harness
    {
        public int StartThreadCalls;
        public int PostCloseCalls;
        public bool DesiredEnabled;
        public OverlayStateMachine Machine { get; }

        public Harness()
        {
            Machine = new OverlayStateMachine(
                onStartThread: () => StartThreadCalls++,
                onPostClose: () => PostCloseCalls++,
                getDesiredEnabled: () => DesiredEnabled
            );
        }
    }

    // === Basic transitions ===

    [Fact]
    public void InitialState_IsOff()
    {
        var h = new Harness();
        Assert.Equal(OverlayStatus.Off, h.Machine.State);
    }

    [Fact]
    public void TurnOn_FromOff_EntersStartingAndStartsThread()
    {
        var h = new Harness();
        h.Machine.Fire(OverlayTrigger.TurnOn);

        Assert.Equal(OverlayStatus.Starting, h.Machine.State);
        Assert.Equal(1, h.StartThreadCalls);
    }

    [Fact]
    public void Ready_FromStarting_EntersActive()
    {
        var h = new Harness();
        h.Machine.Fire(OverlayTrigger.TurnOn);
        h.Machine.Fire(OverlayTrigger.Ready);

        Assert.Equal(OverlayStatus.Active, h.Machine.State);
    }

    [Fact]
    public void TurnOff_FromActive_EntersStoppingAndPostsClose()
    {
        var h = new Harness();
        h.Machine.Fire(OverlayTrigger.TurnOn);
        h.Machine.Fire(OverlayTrigger.Ready);
        h.Machine.Fire(OverlayTrigger.TurnOff);

        Assert.Equal(OverlayStatus.Stopping, h.Machine.State);
        Assert.Equal(1, h.PostCloseCalls);
    }

    [Fact]
    public void ThreadExited_FromStopping_SettlesOffWhenDesiredFalse()
    {
        var h = new Harness { DesiredEnabled = false };
        h.Machine.Fire(OverlayTrigger.TurnOn);
        h.Machine.Fire(OverlayTrigger.Ready);
        h.Machine.Fire(OverlayTrigger.TurnOff);
        h.Machine.Fire(OverlayTrigger.ThreadExited);

        Assert.Equal(OverlayStatus.Off, h.Machine.State);
        // Only one StartThread call (the original TurnOn) — no reconcile respawn.
        Assert.Equal(1, h.StartThreadCalls);
    }

    // === Reverse-intent mid-transition ===

    [Fact]
    public void TurnOn_DuringStopping_IsDeferredThenRespawnsOnThreadExit()
    {
        var h = new Harness();
        // Start and confirm Active
        h.Machine.Fire(OverlayTrigger.TurnOn);
        h.Machine.Fire(OverlayTrigger.Ready);

        // User clicks off
        h.Machine.Fire(OverlayTrigger.TurnOff);
        Assert.Equal(OverlayStatus.Stopping, h.Machine.State);

        // User immediately clicks on again — machine ignores TurnOn in Stopping,
        // but DesiredEnabled flips true and reconcile-on-Off-entry picks it up.
        h.DesiredEnabled = true;
        h.Machine.Fire(OverlayTrigger.TurnOn); // ignored
        Assert.Equal(OverlayStatus.Stopping, h.Machine.State);

        // Old thread exits; Off.OnEntry reconciles by firing TurnOn.
        h.Machine.Fire(OverlayTrigger.ThreadExited);

        Assert.Equal(OverlayStatus.Starting, h.Machine.State);
        Assert.Equal(2, h.StartThreadCalls); // original + reconciled respawn
    }

    [Fact]
    public void TurnOff_DuringStarting_EntersStopping()
    {
        // Click on then immediately off before Ready fires.
        var h = new Harness();
        h.Machine.Fire(OverlayTrigger.TurnOn);
        h.Machine.Fire(OverlayTrigger.TurnOff);

        Assert.Equal(OverlayStatus.Stopping, h.Machine.State);
        Assert.Equal(1, h.PostCloseCalls);
    }

    // === External close ===

    [Fact]
    public void ThreadExited_FromActive_EntersOff()
    {
        // User closes overlay window via Alt+F4 — thread exits without TurnOff
        // being fired first.
        var h = new Harness { DesiredEnabled = false }; // host flips desired off
        h.Machine.Fire(OverlayTrigger.TurnOn);
        h.Machine.Fire(OverlayTrigger.Ready);
        h.Machine.Fire(OverlayTrigger.ThreadExited);

        Assert.Equal(OverlayStatus.Off, h.Machine.State);
    }

    [Fact]
    public void ThreadExited_FromStarting_EntersOff()
    {
        // External close during startup.
        var h = new Harness { DesiredEnabled = false };
        h.Machine.Fire(OverlayTrigger.TurnOn);
        h.Machine.Fire(OverlayTrigger.ThreadExited);

        Assert.Equal(OverlayStatus.Off, h.Machine.State);
    }

    // === Failed / retry ===

    [Fact]
    public void Failed_FromStarting_EntersFailed()
    {
        var h = new Harness();
        h.Machine.Fire(OverlayTrigger.TurnOn);
        h.Machine.Fire(OverlayTrigger.Failed);

        Assert.Equal(OverlayStatus.Failed, h.Machine.State);
    }

    [Fact]
    public void TurnOn_FromFailed_RestartsThread()
    {
        var h = new Harness();
        h.Machine.Fire(OverlayTrigger.TurnOn);
        h.Machine.Fire(OverlayTrigger.Failed);
        h.Machine.Fire(OverlayTrigger.TurnOn);

        Assert.Equal(OverlayStatus.Starting, h.Machine.State);
        Assert.Equal(2, h.StartThreadCalls);
    }

    [Fact]
    public void TurnOff_FromFailed_EntersOffWithoutRestart()
    {
        var h = new Harness { DesiredEnabled = false };
        h.Machine.Fire(OverlayTrigger.TurnOn);
        h.Machine.Fire(OverlayTrigger.Failed);
        h.Machine.Fire(OverlayTrigger.TurnOff);

        Assert.Equal(OverlayStatus.Off, h.Machine.State);
        Assert.Equal(1, h.StartThreadCalls); // no reconcile respawn
    }

    // === Idempotency / irrelevant triggers ===

    [Fact]
    public void TurnOn_WhileStarting_IsIgnored()
    {
        var h = new Harness();
        h.Machine.Fire(OverlayTrigger.TurnOn);
        h.Machine.Fire(OverlayTrigger.TurnOn);

        Assert.Equal(OverlayStatus.Starting, h.Machine.State);
        Assert.Equal(1, h.StartThreadCalls);
    }

    [Fact]
    public void TurnOff_WhileOff_IsIgnored()
    {
        var h = new Harness();
        h.Machine.Fire(OverlayTrigger.TurnOff);

        Assert.Equal(OverlayStatus.Off, h.Machine.State);
        Assert.Equal(0, h.PostCloseCalls);
    }
}
