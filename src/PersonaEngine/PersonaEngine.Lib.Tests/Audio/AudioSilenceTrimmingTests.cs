using PersonaEngine.Lib.Audio;
using Xunit;

namespace PersonaEngine.Lib.Tests.Audio;

public class AudioSilenceTrimmingTests
{
    [Fact]
    public void Trim_LeadingAndTrailingSilence_Trimmed()
    {
        // 100 silent samples, 50 loud, 100 silent
        var audio = new float[250];
        for (var i = 100; i < 150; i++)
        {
            audio[i] = 0.5f;
        }

        var result = AudioSilenceTrimmer.Trim(
            audio.AsMemory(),
            threshold: 0.01f,
            paddingSamples: 10
        );

        // startIndex = 100 - 10 = 90, endIndex = 149 + 10 = 159, length = 70
        Assert.Equal(70, result.Length);
    }

    [Fact]
    public void Trim_AllSilent_ReturnsOriginal()
    {
        var audio = new float[100];

        var result = AudioSilenceTrimmer.Trim(audio.AsMemory());

        Assert.Equal(100, result.Length);
    }

    [Fact]
    public void Trim_Empty_ReturnsEmpty()
    {
        var result = AudioSilenceTrimmer.Trim(Memory<float>.Empty);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Trim_NoSilence_ReturnsOriginal()
    {
        var audio = Enumerable.Repeat(0.5f, 100).ToArray();

        var result = AudioSilenceTrimmer.Trim(
            audio.AsMemory(),
            threshold: 0.01f,
            paddingSamples: 10
        );

        Assert.Equal(100, result.Length);
    }

    [Fact]
    public void Trim_TooShortForPadding_ReturnsOriginal()
    {
        var audio = new float[] { 0f, 0f, 1f, 0f, 0f };

        var result = AudioSilenceTrimmer.Trim(
            audio.AsMemory(),
            threshold: 0.01f,
            paddingSamples: 512
        );

        Assert.Equal(5, result.Length);
    }
}
