using PersonaEngine.Lib.Core.Conversation.Implementations.Session;
using Xunit;

namespace PersonaEngine.Lib.Tests.Core.Conversation.Session;

public class TurnMetricsTrackerTests
{
    [Fact]
    public void GetTurnElapsedMs_returns_null_before_StartTurn()
    {
        var tracker = new TurnMetricsTracker();

        Assert.Null(tracker.GetTurnElapsedMs());
    }

    [Fact]
    public void GetTurnElapsedMs_returns_positive_value_after_StartTurn()
    {
        var tracker = new TurnMetricsTracker();
        tracker.StartTurn(0);

        var elapsed = tracker.GetTurnElapsedMs();

        Assert.NotNull(elapsed);
        Assert.True(elapsed!.Value >= 0);
    }

    [Fact]
    public void GetTurnElapsedMs_does_not_stop_the_turn_stopwatch()
    {
        var tracker = new TurnMetricsTracker();
        tracker.StartTurn(0);

        var first = tracker.GetTurnElapsedMs();
        Thread.Sleep(15);
        var second = tracker.GetTurnElapsedMs();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(
            second!.Value > first!.Value,
            "Stopwatch should still be running; second read must be greater than first."
        );
    }

    [Fact]
    public void GetTurnElapsedMs_returns_null_after_Reset()
    {
        var tracker = new TurnMetricsTracker();
        tracker.StartTurn(0);

        Assert.NotNull(tracker.GetTurnElapsedMs());

        tracker.Reset();

        Assert.Null(tracker.GetTurnElapsedMs());
    }
}
