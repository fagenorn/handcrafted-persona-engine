using System.Numerics;
using FontStashSharp;
using PersonaEngine.Lib.UI.Text.Subtitles;
using Xunit;

namespace PersonaEngine.Lib.Tests.TTS.Subtitles;

public class PopAnimatorTests
{
    private readonly PopAnimator _animator = new();

    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    public void CalculateScale_ClampedInput_ReturnsNonNegative(float progress)
    {
        var scale = _animator.CalculateScale(progress);

        Assert.True(scale.X >= 0f);
        Assert.True(scale.Y >= 0f);
        Assert.Equal(scale.X, scale.Y);
    }

    [Fact]
    public void CalculateScale_AtEnd_ReturnsOne()
    {
        var scale = _animator.CalculateScale(1.0f);

        Assert.Equal(1.0f, scale.X, 0.001f);
    }

    [Fact]
    public void CalculateScale_AtStart_ReturnsZero()
    {
        var scale = _animator.CalculateScale(0.0f);

        Assert.Equal(0.0f, scale.X, 0.001f);
    }

    [Fact]
    public void CalculateScale_ClampsNegativeProgress()
    {
        var scale = _animator.CalculateScale(-0.5f);
        var scaleAtZero = _animator.CalculateScale(0.0f);

        Assert.Equal(scaleAtZero, scale);
    }

    [Fact]
    public void CalculateScale_ClampsOverOneProgress()
    {
        var scale = _animator.CalculateScale(1.5f);
        var scaleAtOne = _animator.CalculateScale(1.0f);

        Assert.Equal(scaleAtOne, scale);
    }

    [Fact]
    public void LerpColor_AtStart_ReturnsStartColor()
    {
        var start = new FSColor(255, 0, 0, 255);
        var end = new FSColor(0, 0, 255, 255);

        var result = PopAnimator.LerpColor(start, end, 0.0f);

        Assert.Equal(start, result);
    }

    [Fact]
    public void LerpColor_AtEnd_ReturnsEndColor()
    {
        var start = new FSColor(255, 0, 0, 255);
        var end = new FSColor(0, 0, 255, 255);

        var result = PopAnimator.LerpColor(start, end, 1.0f);

        Assert.Equal(end, result);
    }

    [Fact]
    public void LerpColor_AtMidpoint_ReturnsMidColor()
    {
        var start = new FSColor(0, 0, 0, 0);
        var end = new FSColor(200, 100, 50, 254);

        var result = PopAnimator.LerpColor(start, end, 0.5f);

        Assert.Equal(100, result.R);
        Assert.Equal(50, result.G);
        Assert.Equal(25, result.B);
        Assert.Equal(127, result.A);
    }
}
