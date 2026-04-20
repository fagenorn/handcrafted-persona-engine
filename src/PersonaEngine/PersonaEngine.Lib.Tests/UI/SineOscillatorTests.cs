using PersonaEngine.Lib.UI.ControlPanel;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI;

public class SineOscillatorTests
{
    [Fact]
    public void Sample_AtZero_ReturnsCenter()
    {
        var osc = new SineOscillator(center: 0.5f, amplitude: 0.2f, frequencyHz: 1f);
        Assert.Equal(0.5f, osc.Sample(0f), precision: 4);
    }

    [Fact]
    public void Sample_AtQuarterPeriod_ReturnsCenterPlusAmplitude()
    {
        var osc = new SineOscillator(center: 0.5f, amplitude: 0.2f, frequencyHz: 1f);
        Assert.Equal(0.7f, osc.Sample(0.25f), precision: 4);
    }

    [Fact]
    public void Sample_AtHalfPeriod_ReturnsCenter()
    {
        var osc = new SineOscillator(center: 0.5f, amplitude: 0.2f, frequencyHz: 1f);
        Assert.Equal(0.5f, osc.Sample(0.5f), precision: 3);
    }

    [Fact]
    public void Sample_AtThreeQuarterPeriod_ReturnsCenterMinusAmplitude()
    {
        var osc = new SineOscillator(center: 0.5f, amplitude: 0.2f, frequencyHz: 1f);
        Assert.Equal(0.3f, osc.Sample(0.75f), precision: 4);
    }

    [Fact]
    public void Sample_DifferentFrequency_ScalesCorrectly()
    {
        var osc = new SineOscillator(center: 0f, amplitude: 1f, frequencyHz: 2f);
        Assert.Equal(1f, osc.Sample(0.125f), precision: 4);
    }
}
