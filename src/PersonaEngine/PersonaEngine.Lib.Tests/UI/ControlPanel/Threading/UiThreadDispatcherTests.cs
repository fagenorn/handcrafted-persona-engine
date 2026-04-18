using Microsoft.Extensions.Logging.Abstractions;
using PersonaEngine.Lib.UI.ControlPanel.Threading;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.ControlPanel.Threading;

public class UiThreadDispatcherTests
{
    [Fact]
    public void DrainPending_RunsEnqueuedActionsInFifoOrder()
    {
        var dispatcher = new UiThreadDispatcher(NullLogger<UiThreadDispatcher>.Instance);
        var calls = new List<int>();
        dispatcher.Post(() => calls.Add(1));
        dispatcher.Post(() => calls.Add(2));
        dispatcher.Post(() => calls.Add(3));

        dispatcher.DrainPending();

        Assert.Equal(new[] { 1, 2, 3 }, calls);
    }

    [Fact]
    public void DrainPending_BoundsPerFrameBatch()
    {
        var dispatcher = new UiThreadDispatcher(NullLogger<UiThreadDispatcher>.Instance);
        const int total = 200;
        var calls = 0;
        for (var i = 0; i < total; i++)
        {
            dispatcher.Post(() => calls++);
        }

        dispatcher.DrainPending();
        Assert.Equal(UiThreadDispatcher.MaxPerFrame, calls);

        while (calls < total)
        {
            dispatcher.DrainPending();
        }

        Assert.Equal(total, calls);
    }

    [Fact]
    public void DrainPending_SwallowsExceptionsAndContinues()
    {
        var dispatcher = new UiThreadDispatcher(NullLogger<UiThreadDispatcher>.Instance);
        var ran = false;
        dispatcher.Post(() => throw new InvalidOperationException("boom"));
        dispatcher.Post(() => ran = true);

        dispatcher.DrainPending();

        Assert.True(ran);
    }
}
