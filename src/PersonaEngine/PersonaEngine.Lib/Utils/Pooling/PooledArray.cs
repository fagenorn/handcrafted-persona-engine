using System.Buffers;

namespace PersonaEngine.Lib.Utils.Pooling;

/// <summary>
///     A disposable wrapper around <see cref="ArrayPool{T}" /> that returns the
///     rented array on disposal. Use with <c>using</c> for automatic cleanup.
/// </summary>
public struct PooledArray<T> : IDisposable
{
    private readonly T[] _array;
    private readonly int _length;
    private bool _disposed;

    private PooledArray(T[] array, int length)
    {
        _array = array;
        _length = length;
        _disposed = false;
    }

    /// <summary>The requested number of elements (not the backing array length).</summary>
    public int Length => _length;

    /// <summary>A span over exactly <see cref="Length" /> elements.</summary>
    public Span<T> Span => _array.AsSpan(0, _length);

    /// <summary>A memory region over exactly <see cref="Length" /> elements.</summary>
    public Memory<T> Memory => _array.AsMemory(0, _length);

    /// <summary>The backing array. Length may exceed <see cref="Length" />.</summary>
    public T[] Array => _array;

    /// <summary>Rents an array of at least <paramref name="length" /> elements from the shared pool.</summary>
    public static PooledArray<T> Rent(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        return new PooledArray<T>(ArrayPool<T>.Shared.Rent(length), length);
    }

    /// <summary>Returns the backing array to the pool.</summary>
    public void Dispose()
    {
        if (!_disposed && _array is not null)
        {
            _disposed = true;
            ArrayPool<T>.Shared.Return(_array, ArrayPoolConfig.ClearOnReturn);
        }
    }
}
