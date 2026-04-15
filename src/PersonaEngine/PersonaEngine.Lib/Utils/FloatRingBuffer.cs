namespace PersonaEngine.Lib.Utils;

/// <summary>
///     Fixed-capacity circular buffer of <see cref="float" /> values. Mutable struct — owners
///     should hold it as a field, not pass it by value. Used by meter widgets and amplitude
///     providers for a short trailing history of samples.
/// </summary>
internal struct FloatRingBuffer
{
    private readonly float[] _buffer;
    private int _head;

    public FloatRingBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        _buffer = new float[capacity];
        _head = 0;
    }

    /// <summary>
    ///     The underlying buffer in raw slot order. Newest value is at
    ///     <c>(Head - 1 + Capacity) % Capacity</c>.
    /// </summary>
    public ReadOnlySpan<float> Values => _buffer;

    /// <summary>
    ///     Index of the next write slot. Advances by 1 per <see cref="Push" /> call and wraps.
    /// </summary>
    public int Head => _head;

    public int Capacity => _buffer.Length;

    /// <summary>Writes <paramref name="value" /> into the current head slot and advances the head.</summary>
    public void Push(float value)
    {
        _buffer[_head] = value;
        _head = (_head + 1) % _buffer.Length;
    }
}
