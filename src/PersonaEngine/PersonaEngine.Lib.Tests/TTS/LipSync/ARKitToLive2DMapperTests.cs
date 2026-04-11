using PersonaEngine.Lib.TTS.Synthesis.LipSync;
using PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;
using Xunit;

namespace PersonaEngine.Lib.Tests.TTS.LipSync;

public class ARKitToLive2DMapperTests
{
    private const int WeightCount = 52;
    private const float Tolerance = 1e-4f;

    private static float[] MakeWeights() => new float[WeightCount];

    [Fact]
    public void AllZeros_ReturnsNeutralFrame()
    {
        var weights = MakeWeights();
        var frame = ARKitToLive2DMapper.Map(weights);

        Assert.Equal(0f, frame.MouthOpenY, Tolerance);
        Assert.Equal(0f, frame.JawOpen, Tolerance);
        // MouthForm with all zeros: (2 - 0 - 0 - 0 + 0 + 0 + 0) / 4 = 0.5
        Assert.Equal(0.5f, frame.MouthForm, Tolerance);
        Assert.Equal(0f, frame.MouthShrug, Tolerance);
        Assert.Equal(0f, frame.MouthFunnel, Tolerance);
        Assert.Equal(0f, frame.MouthPuckerWiden, Tolerance);
        // MouthPressLipOpen: CenterWeighted(0, -1.3, 1.3) = 0
        Assert.Equal(0f, frame.MouthPressLipOpen, Tolerance);
        Assert.Equal(0f, frame.MouthX, Tolerance);
        Assert.Equal(0f, frame.CheekPuffC, Tolerance);
        // Eyes: (0.5 + 0 + 0) * 2 = 1.0
        Assert.Equal(1.0f, frame.EyeLOpen!.Value, Tolerance);
        Assert.Equal(1.0f, frame.EyeROpen!.Value, Tolerance);
        Assert.Equal(0f, frame.BrowLY!.Value, Tolerance);
        Assert.Equal(0f, frame.BrowRY!.Value, Tolerance);
    }

    [Fact]
    public void JawOpenHalf_AppliesEaseIn()
    {
        var weights = MakeWeights();
        weights[17] = 0.5f; // JawOpen

        var frame = ARKitToLive2DMapper.Map(weights);
        // ease_in(0.5) = 0.5 * (2 - 0.5) = 0.75
        Assert.Equal(0.75f, frame.JawOpen, Tolerance);
        // MouthOpenY: ease_in(clip(0.5 - 0 - 0 + 0, 0, 1)) = ease_in(0.5) = 0.75
        Assert.Equal(0.75f, frame.MouthOpenY, Tolerance);
    }

    [Fact]
    public void JawOpenWithMouthClose_MouthOpenYNearZero()
    {
        var weights = MakeWeights();
        weights[17] = 1.0f; // JawOpen
        weights[18] = 1.0f; // MouthClose

        var frame = ARKitToLive2DMapper.Map(weights);
        // MouthOpenY: ease_in(clip(1.0 - 1.0, 0, 1)) = ease_in(0) = 0
        Assert.Equal(0f, frame.MouthOpenY, Tolerance);
    }

    [Fact]
    public void SmileBoth_MouthFormHigh()
    {
        var weights = MakeWeights();
        weights[23] = 0.8f; // SmileL
        weights[24] = 0.8f; // SmileR

        var frame = ARKitToLive2DMapper.Map(weights);
        // (2 - 0 - 0 - 0 + 0.8 + 0.8 + 0) / 4 = 3.6 / 4 = 0.9
        Assert.Equal(0.9f, frame.MouthForm, Tolerance);
    }

    [Fact]
    public void FrownBoth_MouthFormLow()
    {
        var weights = MakeWeights();
        weights[25] = 0.8f; // FrownL
        weights[26] = 0.8f; // FrownR

        var frame = ARKitToLive2DMapper.Map(weights);
        // (2 - 0.8 - 0.8 - 0 + 0 + 0 + 0) / 4 = 0.4 / 4 = 0.1
        Assert.Equal(0.1f, frame.MouthForm, Tolerance);
    }

    [Fact]
    public void MouthPucker_PuckerWidenNegative()
    {
        var weights = MakeWeights();
        weights[20] = 0.7f; // MouthPucker

        var frame = ARKitToLive2DMapper.Map(weights);
        // (0 + 0)*2 - 0.7 = -0.7
        Assert.Equal(-0.7f, frame.MouthPuckerWiden, Tolerance);
    }

    [Fact]
    public void EyeBlinkLeft_EyeLOpenClosed()
    {
        var weights = MakeWeights();
        weights[0] = 1.0f; // EyeBlinkLeft

        var frame = ARKitToLive2DMapper.Map(weights);
        // (0.5 + 1.0 * -0.8 + 0) * 2 = (0.5 - 0.8) * 2 = -0.6 -> clamped to 0
        Assert.Equal(0f, frame.EyeLOpen!.Value, Tolerance);
    }

    [Fact]
    public void EyeWideLeft_EyeLOpenWide()
    {
        var weights = MakeWeights();
        weights[6] = 1.0f; // EyeWideLeft

        var frame = ARKitToLive2DMapper.Map(weights);
        // (0.5 + 0 + 1.0 * 0.8) * 2 = 1.3 * 2 = 2.6 -> clamped to 2.0
        Assert.Equal(2.0f, frame.EyeLOpen!.Value, Tolerance);
    }

    [Fact]
    public void BrowUp_BrowYPositive()
    {
        var weights = MakeWeights();
        weights[44] = 0.6f; // BrowOuterUpLeft

        var frame = ARKitToLive2DMapper.Map(weights);
        // (0.6 - 0) + 0 = 0.6
        Assert.Equal(0.6f, frame.BrowLY!.Value, Tolerance);
    }

    [Fact]
    public void AllWeightsOne_AllParamsWithinValidRanges()
    {
        var weights = MakeWeights();
        for (var i = 0; i < WeightCount; i++)
            weights[i] = 1.0f;

        var frame = ARKitToLive2DMapper.Map(weights);

        Assert.InRange(frame.MouthOpenY, 0f, 1f);
        Assert.InRange(frame.JawOpen, 0f, 1f);
        Assert.InRange(frame.MouthShrug, 0f, 1f);
        Assert.InRange(frame.MouthFunnel, 0f, 1f);
        Assert.InRange(frame.CheekPuffC, 0f, 1f);
        Assert.InRange(frame.EyeLOpen!.Value, 0f, 2f); // Aria range
        Assert.InRange(frame.EyeROpen!.Value, 0f, 2f);
        Assert.InRange(frame.MouthForm, -1f, 1f);
        Assert.InRange(frame.MouthPuckerWiden, -1f, 1f);
        Assert.InRange(frame.MouthPressLipOpen, -1.3f, 1.3f);
        Assert.InRange(frame.MouthX, -1f, 1f);
        Assert.InRange(frame.BrowLY!.Value, -1f, 1f);
        Assert.InRange(frame.BrowRY!.Value, -1f, 1f);
    }
}
