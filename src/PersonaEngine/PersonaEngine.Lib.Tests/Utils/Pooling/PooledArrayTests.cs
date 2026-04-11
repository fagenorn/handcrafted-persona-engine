using PersonaEngine.Lib.Utils.Pooling;
using Xunit;

namespace PersonaEngine.Lib.Tests.Utils.Pooling;

public class PooledArrayTests
{
    [Fact]
    public void Rent_ReturnsCorrectLength()
    {
        using var buffer = PooledArray<float>.Rent(100);
        Assert.Equal(100, buffer.Length);
    }

    [Fact]
    public void Span_HasCorrectLength()
    {
        using var buffer = PooledArray<float>.Rent(50);
        Assert.Equal(50, buffer.Span.Length);
    }

    [Fact]
    public void Memory_HasCorrectLength()
    {
        using var buffer = PooledArray<float>.Rent(50);
        Assert.Equal(50, buffer.Memory.Length);
    }

    [Fact]
    public void Span_IsWritable()
    {
        using var buffer = PooledArray<float>.Rent(10);
        buffer.Span[0] = 42.0f;
        buffer.Span[9] = 99.0f;
        Assert.Equal(42.0f, buffer.Span[0]);
        Assert.Equal(99.0f, buffer.Span[9]);
    }

    [Fact]
    public void Array_ExposesBackingArray()
    {
        using var buffer = PooledArray<float>.Rent(10);
        Assert.NotNull(buffer.Array);
        Assert.True(buffer.Array.Length >= 10);
    }

    [Fact]
    public void Dispose_DoesNotThrowOnDoubleDispose()
    {
        var buffer = PooledArray<float>.Rent(10);
        buffer.Dispose();
        buffer.Dispose();
    }
}
