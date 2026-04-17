using NSubstitute;
using PersonaEngine.Lib.Health;
using PersonaEngine.Lib.Health.Probes;
using PersonaEngine.Lib.TTS.Synthesis;
using Xunit;

namespace PersonaEngine.Lib.Tests.Health;

public class TtsHealthProbeTests
{
    [Fact]
    public void Healthy_WhenEngineReady()
    {
        var tts = Substitute.For<ITtsEngine>();
        tts.IsReady.Returns(true);
        tts.LastInitError.Returns((string?)null);

        using var probe = new TtsHealthProbe(tts);

        Assert.Equal(SubsystemHealth.Healthy, probe.Current.Health);
        Assert.Equal("Ready", probe.Current.Label);
        Assert.Null(probe.Current.Detail);
    }

    [Fact]
    public void Failed_WhenInitError()
    {
        var tts = Substitute.For<ITtsEngine>();
        tts.IsReady.Returns(false);
        tts.LastInitError.Returns("ONNX model missing at C:\\foo");

        using var probe = new TtsHealthProbe(tts);

        Assert.Equal(SubsystemHealth.Failed, probe.Current.Health);
        Assert.Equal("Init failed", probe.Current.Label);
        Assert.Contains("ONNX model missing", probe.Current.Detail);
    }

    [Fact]
    public void Unknown_WhenNotReadyAndNoError()
    {
        var tts = Substitute.For<ITtsEngine>();
        tts.IsReady.Returns(false);
        tts.LastInitError.Returns((string?)null);

        using var probe = new TtsHealthProbe(tts);

        Assert.Equal(SubsystemHealth.Unknown, probe.Current.Health);
        Assert.Equal("Warming up", probe.Current.Label);
        Assert.Null(probe.Current.Detail);
    }

    [Fact]
    public void StatusChanged_Fires_OnlyOnTransition()
    {
        var tts = Substitute.For<ITtsEngine>();
        tts.IsReady.Returns(true);
        tts.LastInitError.Returns((string?)null);

        using var probe = new TtsHealthProbe(tts);
        var fired = 0;
        probe.StatusChanged += _ => fired++;

        // Same state → no fire.
        tts.ReadyChanged += Raise.Event<Action>();
        Assert.Equal(0, fired);

        // Flip to failed → fire once.
        tts.IsReady.Returns(false);
        tts.LastInitError.Returns("boom");
        tts.ReadyChanged += Raise.Event<Action>();
        Assert.Equal(1, fired);
    }
}
