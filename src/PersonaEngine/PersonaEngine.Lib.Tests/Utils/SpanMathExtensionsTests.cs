using PersonaEngine.Lib.Utils;
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
}
