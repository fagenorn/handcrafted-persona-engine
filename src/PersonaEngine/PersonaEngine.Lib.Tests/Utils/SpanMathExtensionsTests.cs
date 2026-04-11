using PersonaEngine.Lib.Utils.Numerics;
using Xunit;

namespace PersonaEngine.Lib.Tests.Utils;

public class SpanMathExtensionsTests
{
    [Fact]
    public void ArgMax_SingleElement_ReturnsZero()
    {
        ReadOnlySpan<float> values = [42f];
        Assert.Equal(0, values.ArgMax());
    }

    [Fact]
    public void ArgMax_MaxAtEnd_ReturnsLastIndex()
    {
        ReadOnlySpan<float> values = [1f, 2f, 3f, 4f, 5f];
        Assert.Equal(4, values.ArgMax());
    }

    [Fact]
    public void ArgMax_MaxAtStart_ReturnsZero()
    {
        ReadOnlySpan<float> values = [99f, 1f, 2f, 3f];
        Assert.Equal(0, values.ArgMax());
    }

    [Fact]
    public void ArgMax_MaxInMiddle_ReturnsCorrectIndex()
    {
        ReadOnlySpan<float> values = [1f, 2f, 99f, 3f, 4f];
        Assert.Equal(2, values.ArgMax());
    }

    [Fact]
    public void ArgMax_NegativeValues_ReturnsLeastNegative()
    {
        ReadOnlySpan<float> values = [-5f, -1f, -3f, -2f];
        Assert.Equal(1, values.ArgMax());
    }

    [Fact]
    public void ArgMax_DuplicateMax_ReturnsFirstOccurrence()
    {
        ReadOnlySpan<float> values = [1f, 5f, 5f, 3f];
        Assert.Equal(1, values.ArgMax());
    }

    [Fact]
    public void ArgMax_AllNegativeInfinity_ReturnsZero()
    {
        ReadOnlySpan<float> values = [float.NegativeInfinity, float.NegativeInfinity];
        Assert.Equal(0, values.ArgMax());
    }

    [Fact]
    public void LogSoftmaxInPlace_UniformDistribution_AllEqual()
    {
        Span<float> logits = [1f, 1f, 1f, 1f];
        SpanMathExtensions.LogSoftmaxInPlace(logits);

        var expected = -MathF.Log(4f);
        for (var i = 0; i < logits.Length; i++)
        {
            Assert.Equal(expected, logits[i], 1e-5f);
        }
    }

    [Fact]
    public void LogSoftmaxInPlace_SumsToOne_InProbabilitySpace()
    {
        Span<float> logits = [2f, 1f, 0.5f];
        SpanMathExtensions.LogSoftmaxInPlace(logits);

        var sumProb = 0f;
        for (var i = 0; i < logits.Length; i++)
        {
            sumProb += MathF.Exp(logits[i]);
        }

        Assert.Equal(1f, sumProb, 1e-5f);
    }

    [Fact]
    public void LogSoftmaxInPlace_AllNegative_StillSumsToOne()
    {
        Span<float> logits = [-10f, -20f, -30f];
        SpanMathExtensions.LogSoftmaxInPlace(logits);

        var sumProb = 0f;
        for (var i = 0; i < logits.Length; i++)
        {
            sumProb += MathF.Exp(logits[i]);
        }

        Assert.Equal(1f, sumProb, 1e-5f);
    }

    [Fact]
    public void LogSoftmaxInPlace_LargeValues_NumericallyStable()
    {
        Span<float> logits = [1000f, 1001f, 1002f];
        SpanMathExtensions.LogSoftmaxInPlace(logits);

        for (var i = 0; i < logits.Length; i++)
        {
            Assert.False(float.IsNaN(logits[i]));
            Assert.False(float.IsPositiveInfinity(logits[i]));
            Assert.True(logits[i] <= 0f);
        }
    }

    [Fact]
    public void LogSoftmaxInPlace_PreservesOrdering()
    {
        Span<float> logits = [3f, 1f, 2f];
        SpanMathExtensions.LogSoftmaxInPlace(logits);

        Assert.True(logits[0] > logits[2]);
        Assert.True(logits[2] > logits[1]);
    }
}
