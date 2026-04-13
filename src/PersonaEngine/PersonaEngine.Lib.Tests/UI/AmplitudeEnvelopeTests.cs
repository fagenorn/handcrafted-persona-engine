using PersonaEngine.Lib.Core.Conversation.Implementations.Adapters.Audio.Output;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI;

public class AmplitudeEnvelopeTests
{
    [Fact]
    public void From_EmptySamples_ReturnsEmptyEnvelope()
    {
        var env = AmplitudeEnvelope.From(ReadOnlySpan<float>.Empty, 24000);
        Assert.Equal(0, env.WindowCount);
        Assert.Equal(0f, env.SampleAt(0f));
        Assert.Equal(0f, env.SampleAt(10f));
    }

    [Fact]
    public void From_ZeroSampleRate_ReturnsEmptyEnvelope()
    {
        var samples = new float[100];
        Array.Fill(samples, 0.5f);
        var env = AmplitudeEnvelope.From(samples, 0);
        Assert.Equal(0, env.WindowCount);
    }

    [Fact]
    public void From_ConstantValue_ProducesConstantEnvelope()
    {
        var samples = new float[24000]; // 1 second at 24kHz
        Array.Fill(samples, 0.5f);
        var env = AmplitudeEnvelope.From(samples, 24000, windowMs: 30f);

        // 30ms windows over 1000ms = ~33 windows, each with RMS = 0.5
        Assert.True(env.WindowCount >= 33);
        Assert.Equal(0.5f, env.SampleAt(0f), precision: 4);
        Assert.Equal(0.5f, env.SampleAt(0.5f), precision: 4);
    }

    [Fact]
    public void SampleAt_NegativeOrBeyondEnd_ClampsToBounds()
    {
        var samples = new float[24000];
        Array.Fill(samples, 0.7f);
        var env = AmplitudeEnvelope.From(samples, 24000);

        Assert.Equal(0.7f, env.SampleAt(-5f), precision: 4); // clamps to first
        Assert.Equal(0.7f, env.SampleAt(100f), precision: 4); // clamps to last
    }

    [Fact]
    public void SampleAt_VaryingAmplitude_ReflectsTimePosition()
    {
        // First half loud (0.8), second half quiet (0.1)
        var samples = new float[24000];
        for (var i = 0; i < 12000; i++)
            samples[i] = 0.8f;
        for (var i = 12000; i < 24000; i++)
            samples[i] = 0.1f;

        var env = AmplitudeEnvelope.From(samples, 24000, windowMs: 30f);

        Assert.True(env.SampleAt(0.1f) > 0.5f); // early = loud
        Assert.True(env.SampleAt(0.9f) < 0.3f); // late = quiet
    }

    [Fact]
    public void WindowDurationSeconds_IsSensible()
    {
        var samples = new float[24000];
        var env = AmplitudeEnvelope.From(samples, 24000, windowMs: 30f);
        Assert.Equal(0.030f, env.WindowDurationSeconds, precision: 3);
    }
}
