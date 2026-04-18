namespace PersonaEngine.Lib.Utils;

/// <summary>
///     Fixed-capacity circular buffer of <see cref="float" /> values. Mutable struct — owners
///     should hold it as a field, not pass it by value. Used by meter widgets and amplitude
///     providers for a short trailing history of samples.
///     <para>
///         Thread-safety: single-writer, single-reader. Concurrent reads while a write is in
///         flight yield a partially-consistent snapshot (one slot may be stale); this is
///         acceptable for UI meters and is the only access pattern in use. Do NOT share across
///         multiple writers.
///     </para>
///     <para>
///         Capacity MUST be a power of two. The hot <see cref="Push" /> path uses
///         <c>&amp; (Capacity - 1)</c> to wrap the head index, avoiding a division. Both
///         production callers (<c>VadProbabilityProvider</c>, <c>MicrophoneAmplitudeProvider</c>)
///         use pow2 capacities (64 / 128).
///     </para>
/// </summary>
internal struct FloatRingBuffer
{
    private readonly float[] _buffer;
    private readonly int _mask;
    private int _head;

    public FloatRingBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        if ((capacity & (capacity - 1)) != 0)
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                "Capacity must be a power of two."
            );
        _buffer = new float[capacity];
        _mask = capacity - 1;
        _head = 0;
    }

    /// <summary>
    ///     The underlying buffer in raw slot order. Newest value is at
    ///     <c>(Head - 1) &amp; (Capacity - 1)</c>.
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
        _head = (_head + 1) & _mask;
    }
}
