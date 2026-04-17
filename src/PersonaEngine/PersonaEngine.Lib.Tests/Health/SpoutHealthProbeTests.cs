using NSubstitute;
using PersonaEngine.Lib.Health;
using PersonaEngine.Lib.Health.Probes;
using PersonaEngine.Lib.UI.Rendering.Spout;
using Xunit;

namespace PersonaEngine.Lib.Tests.Health;

public class SpoutHealthProbeTests
{
    [Fact]
    public void Disabled_WhenNoSendersConfigured()
    {
        var registry = Substitute.For<ISpoutRegistry>();
        registry.ConfiguredSenderCount.Returns(0);
        registry.ActiveSenderCount.Returns(0);

        using var probe = new SpoutHealthProbe(registry);

        Assert.Equal(SubsystemHealth.Disabled, probe.Current.Health);
        Assert.Equal("No senders", probe.Current.Label);
        Assert.Null(probe.Current.Detail);
    }

    [Fact]
    public void Healthy_WhenAllConfiguredSendersActive()
    {
        var registry = Substitute.For<ISpoutRegistry>();
        registry.ConfiguredSenderCount.Returns(2);
        registry.ActiveSenderCount.Returns(2);

        using var probe = new SpoutHealthProbe(registry);

        Assert.Equal(SubsystemHealth.Healthy, probe.Current.Health);
        Assert.Equal("Streaming", probe.Current.Label);
        Assert.Null(probe.Current.Detail);
    }

    [Fact]
    public void Degraded_WhenSomeSendersFailed()
    {
        var registry = Substitute.For<ISpoutRegistry>();
        registry.ConfiguredSenderCount.Returns(2);
        registry.ActiveSenderCount.Returns(1);

        using var probe = new SpoutHealthProbe(registry);

        Assert.Equal(SubsystemHealth.Degraded, probe.Current.Health);
        Assert.Equal("Partial", probe.Current.Label);
        Assert.Contains("1 of 2", probe.Current.Detail);
    }

    [Fact]
    public void Failed_WhenNoneActive()
    {
        var registry = Substitute.For<ISpoutRegistry>();
        registry.ConfiguredSenderCount.Returns(2);
        registry.ActiveSenderCount.Returns(0);

        using var probe = new SpoutHealthProbe(registry);

        Assert.Equal(SubsystemHealth.Failed, probe.Current.Health);
        Assert.Equal("No active senders", probe.Current.Label);
        Assert.Contains("0 of 2", probe.Current.Detail);
    }

    [Fact]
    public void StatusChanged_Fires_OnlyOnTransition()
    {
        var registry = Substitute.For<ISpoutRegistry>();
        registry.ConfiguredSenderCount.Returns(2);
        registry.ActiveSenderCount.Returns(2);

        using var probe = new SpoutHealthProbe(registry);
        var fired = 0;
        probe.StatusChanged += _ => fired++;

        // Same state → no fire.
        registry.SendersChanged += Raise.Event<Action>();
        Assert.Equal(0, fired);

        // Flip to Degraded → fire once.
        registry.ActiveSenderCount.Returns(1);
        registry.SendersChanged += Raise.Event<Action>();
        Assert.Equal(1, fired);
    }
}
