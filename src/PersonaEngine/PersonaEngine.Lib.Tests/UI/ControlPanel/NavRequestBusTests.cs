using PersonaEngine.Lib.UI.ControlPanel;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.ControlPanel;

public class NavRequestBusTests
{
    [Fact]
    public void Request_DeliversToSubscriber()
    {
        var bus = new NavRequestBus();
        NavSection? received = null;
        bus.Requested += s => received = s;

        bus.Request(NavSection.LlmConnection);

        Assert.Equal(NavSection.LlmConnection, received);
    }

    [Fact]
    public void Request_WithNoSubscribers_IsNoOp()
    {
        var bus = new NavRequestBus();
        var ex = Record.Exception(() => bus.Request(NavSection.Dashboard));
        Assert.Null(ex);
    }

    [Fact]
    public void Request_DeliversToAllSubscribers()
    {
        var bus = new NavRequestBus();
        var calls = 0;
        bus.Requested += _ => calls++;
        bus.Requested += _ => calls++;

        bus.Request(NavSection.Avatar);

        Assert.Equal(2, calls);
    }
}
