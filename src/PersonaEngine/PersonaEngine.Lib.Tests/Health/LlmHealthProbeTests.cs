using NSubstitute;
using PersonaEngine.Lib.Health;
using PersonaEngine.Lib.Health.Probes;
using PersonaEngine.Lib.LLM.Connection;
using Xunit;

namespace PersonaEngine.Lib.Tests.Health;

public class LlmHealthProbeTests
{
    private static ILlmConnectionProbe Make(LlmProbeStatus text, LlmProbeStatus vision)
    {
        var probe = Substitute.For<ILlmConnectionProbe>();
        probe.TextStatus.Returns(
            new LlmProbeResult(text, null, Array.Empty<string>(), DateTimeOffset.UtcNow)
        );
        probe.VisionStatus.Returns(
            new LlmProbeResult(vision, null, Array.Empty<string>(), DateTimeOffset.UtcNow)
        );
        return probe;
    }

    [Theory]
    [InlineData(LlmProbeStatus.Reachable, LlmProbeStatus.Reachable, SubsystemHealth.Healthy)]
    [InlineData(LlmProbeStatus.Reachable, LlmProbeStatus.Disabled, SubsystemHealth.Healthy)]
    [InlineData(LlmProbeStatus.Reachable, LlmProbeStatus.Unreachable, SubsystemHealth.Degraded)]
    [InlineData(LlmProbeStatus.Reachable, LlmProbeStatus.Unauthorized, SubsystemHealth.Degraded)]
    [InlineData(LlmProbeStatus.ModelMissing, LlmProbeStatus.Reachable, SubsystemHealth.Degraded)]
    [InlineData(LlmProbeStatus.Unreachable, LlmProbeStatus.Reachable, SubsystemHealth.Failed)]
    [InlineData(LlmProbeStatus.Unauthorized, LlmProbeStatus.Reachable, SubsystemHealth.Failed)]
    [InlineData(LlmProbeStatus.InvalidUrl, LlmProbeStatus.Reachable, SubsystemHealth.Failed)]
    [InlineData(LlmProbeStatus.Unknown, LlmProbeStatus.Unknown, SubsystemHealth.Unknown)]
    // Probing handling. Once text is Reachable the card shows "Ready" — if we
    // returned Unknown here it would render as a grey "Ready" and look broken.
    [InlineData(LlmProbeStatus.Probing, LlmProbeStatus.Probing, SubsystemHealth.Unknown)]
    [InlineData(LlmProbeStatus.Probing, LlmProbeStatus.Reachable, SubsystemHealth.Unknown)]
    [InlineData(LlmProbeStatus.Reachable, LlmProbeStatus.Probing, SubsystemHealth.Healthy)]
    // Text Reachable while vision was never probed (channel off / first frame)
    // must stay green — this was the "Ready but grey" bug.
    [InlineData(LlmProbeStatus.Reachable, LlmProbeStatus.Unknown, SubsystemHealth.Healthy)]
    // Vision ModelMissing + InvalidUrl while text is Reachable
    [InlineData(LlmProbeStatus.Reachable, LlmProbeStatus.ModelMissing, SubsystemHealth.Degraded)]
    [InlineData(LlmProbeStatus.Reachable, LlmProbeStatus.InvalidUrl, SubsystemHealth.Degraded)]
    // Text Disabled is an unreachable config in practice (only Vision can be Disabled via
    // LlmConnectionProbe) but pin the defensive outcome in case the invariant breaks.
    [InlineData(LlmProbeStatus.Disabled, LlmProbeStatus.Reachable, SubsystemHealth.Failed)]
    public void Current_MapsTextAndVision(
        LlmProbeStatus text,
        LlmProbeStatus vision,
        SubsystemHealth expected
    )
    {
        var inner = Make(text, vision);
        var probe = new LlmHealthProbe(inner);
        Assert.Equal(expected, probe.Current.Health);
    }

    [Fact]
    public void StatusChanged_Fires_OnlyOnTransition()
    {
        var inner = Substitute.For<ILlmConnectionProbe>();
        inner.TextStatus.Returns(
            new LlmProbeResult(
                LlmProbeStatus.Reachable,
                null,
                Array.Empty<string>(),
                DateTimeOffset.UtcNow
            )
        );
        inner.VisionStatus.Returns(
            new LlmProbeResult(
                LlmProbeStatus.Disabled,
                null,
                Array.Empty<string>(),
                DateTimeOffset.UtcNow
            )
        );

        var probe = new LlmHealthProbe(inner);
        var fired = 0;
        probe.StatusChanged += _ => fired++;

        inner.StatusChanged += Raise.Event<Action<LlmChannel>>(LlmChannel.Text); // same → no fire
        Assert.Equal(0, fired);

        inner.TextStatus.Returns(
            new LlmProbeResult(
                LlmProbeStatus.Unreachable,
                "x",
                Array.Empty<string>(),
                DateTimeOffset.UtcNow
            )
        );
        inner.StatusChanged += Raise.Event<Action<LlmChannel>>(LlmChannel.Text);
        Assert.Equal(1, fired);
    }
}
