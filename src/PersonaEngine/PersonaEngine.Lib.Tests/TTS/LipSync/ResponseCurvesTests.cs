using PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;
using Xunit;

namespace PersonaEngine.Lib.Tests.TTS.LipSync;

public class ResponseCurvesTests
{
    private const float Tolerance = 1e-5f;

    // EaseIn tests
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 0.75f)] // 0.5*(2-0.5) = 0.75
    [InlineData(1f, 1f)] // 1*(2-1) = 1
    [InlineData(0.25f, 0.4375f)] // 0.25*(2-0.25) = 0.4375
    public void EaseIn_KnownValues(float input, float expected)
    {
        Assert.Equal(expected, ResponseCurves.EaseIn(input), Tolerance);
    }

    [Fact]
    public void EaseIn_ClampsNegative()
    {
        Assert.Equal(0f, ResponseCurves.EaseIn(-0.5f), Tolerance);
    }

    [Fact]
    public void EaseIn_ClampsAboveOne()
    {
        Assert.Equal(1f, ResponseCurves.EaseIn(1.5f), Tolerance);
    }

    // CenterWeighted tests
    [Fact]
    public void CenterWeighted_AtZero_ReturnsZero()
    {
        Assert.Equal(0f, ResponseCurves.CenterWeighted(0f, -1.3f, 1.3f), Tolerance);
    }

    [Fact]
    public void CenterWeighted_AtPositiveBound_ReturnsBound()
    {
        // At t=hi, spline evaluates to hi
        var result = ResponseCurves.CenterWeighted(1.3f, -1.3f, 1.3f);
        Assert.Equal(1.3f, result, Tolerance);
    }

    [Fact]
    public void CenterWeighted_AtNegativeBound_ReturnsBound()
    {
        var result = ResponseCurves.CenterWeighted(-1.3f, -1.3f, 1.3f);
        Assert.Equal(-1.3f, result, Tolerance);
    }

    [Fact]
    public void CenterWeighted_ClampsToRange()
    {
        // Values beyond bounds should clamp
        var above = ResponseCurves.CenterWeighted(2.0f, -1.3f, 1.3f);
        var below = ResponseCurves.CenterWeighted(-2.0f, -1.3f, 1.3f);
        Assert.Equal(1.3f, above, Tolerance);
        Assert.Equal(-1.3f, below, Tolerance);
    }

    [Fact]
    public void CenterWeighted_SteepThroughCenter()
    {
        // Near zero, the tangent is 2.0, so small inputs should map close to 2*t
        var result = ResponseCurves.CenterWeighted(0.1f, -1.3f, 1.3f);
        // At t=0.1, expect value close to 0.2 (tangent=2 at origin)
        Assert.True(
            result > 0.15f && result < 0.25f,
            $"Expected CenterWeighted(0.1) near 0.2, got {result}"
        );
    }
}
