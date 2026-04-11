using PersonaEngine.Lib.TTS.Synthesis.LipSync;
using PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;
using Xunit;

namespace PersonaEngine.Lib.Tests.TTS.LipSync;

public class ParamSmootherTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void Smooth_FirstFrame_ReturnsUnchanged()
    {
        var smoother = new ParamSmoother();
        var input = new LipSyncFrame { MouthOpenY = 0.8f, JawOpen = 0.5f };

        var result = smoother.Smooth(in input);

        Assert.Equal(0.8f, result.MouthOpenY, Tolerance);
        Assert.Equal(0.5f, result.JawOpen, Tolerance);
    }

    [Fact]
    public void Smooth_SecondFrame_AppliesEMA()
    {
        var smoother = new ParamSmoother();

        var frame1 = new LipSyncFrame { MouthOpenY = 1.0f };
        smoother.Smooth(in frame1);

        var frame2 = new LipSyncFrame { MouthOpenY = 0.0f };
        var result = smoother.Smooth(in frame2);

        // EMA: prev * factor + input * (1 - factor)
        // = 1.0 * 0.15 + 0.0 * 0.85 = 0.15
        Assert.Equal(0.15f, result.MouthOpenY, Tolerance);
    }

    [Fact]
    public void Smooth_BrowY_HeavySmoothing()
    {
        var smoother = new ParamSmoother();

        var frame1 = new LipSyncFrame { BrowLY = 1.0f };
        smoother.Smooth(in frame1);

        var frame2 = new LipSyncFrame { BrowLY = 0.0f };
        var result = smoother.Smooth(in frame2);

        // BrowY factor = 0.4: prev * 0.4 + input * 0.6 = 1.0 * 0.4 + 0.0 * 0.6 = 0.4
        Assert.Equal(0.4f, result.BrowLY!.Value, Tolerance);
    }

    [Fact]
    public void Smooth_CheekPuff_NoSmoothing()
    {
        var smoother = new ParamSmoother();

        var frame1 = new LipSyncFrame { CheekPuffC = 1.0f };
        smoother.Smooth(in frame1);

        var frame2 = new LipSyncFrame { CheekPuffC = 0.0f };
        var result = smoother.Smooth(in frame2);

        // CheekPuff factor = 0.0 → passes through unchanged
        Assert.Equal(0.0f, result.CheekPuffC, Tolerance);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var smoother = new ParamSmoother();

        var frame1 = new LipSyncFrame { MouthOpenY = 1.0f };
        smoother.Smooth(in frame1);

        smoother.Reset();

        // After reset, next frame should pass through unchanged
        var frame2 = new LipSyncFrame { MouthOpenY = 0.5f };
        var result = smoother.Smooth(in frame2);
        Assert.Equal(0.5f, result.MouthOpenY, Tolerance);
    }
}
