using System;
using System.Runtime.CompilerServices;

namespace PersonaEngine.Lib.Music.AudioSeperator;

/// <summary>
/// Single-producer/single-consumer float ring buffer with head-relative in-place accumulation.
/// - Write(ReadOnlySpan&lt;float&gt;)/WriteOne appends elements.
/// - ReadOne consumes one element (and clears it).
/// - ReadInterleavedToPlanar reads frames of interleaved audio into planar [C][frames].
/// - AddAtFromHead lets you += into the existing pending region (required for proper OLA).
///
/// Capacity is rounded up to a power of two. If writes exceed capacity, the oldest data is
/// dropped (read index is moved forward) to keep Length ≤ Capacity.
/// </summary>
internal sealed class FloatRingBuffer : IDisposable
{
    private readonly float[] _buf;
    private readonly int _mask; // capacity - 1 (power-of-two)
    private long _write; // absolute write index (monotonic)
    private long _read; // absolute read index (monotonic)

    public int Capacity => _buf.Length;

    /// <summary>
    /// Number of elements available to read (in floats, not frames).
    /// </summary>
    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (int)(_write - _read);
    }

    public FloatRingBuffer(int capacityPow2)
    {
        // Round up to power-of-two
        int cap = 1;
        while (cap < capacityPow2)
            cap <<= 1;

        _buf = GC.AllocateUninitializedArray<float>(cap, pinned: false);
        _mask = cap - 1;
        _write = 0;
        _read = 0;
    }

    /// <summary>
    /// Append a span of samples.
    /// If the write would overflow capacity, the oldest data is dropped.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySpan<float> src)
    {
        int n = src.Length;
        if (n == 0)
            return;

        int cap = _buf.Length;
        int w = (int)(_write & _mask);

        // First contiguous chunk until end of buffer
        int first = Math.Min(cap - w, n);
        src.Slice(0, first).CopyTo(_buf.AsSpan(w, first));

        // Remainder wraps to start
        int rem = n - first;
        if (rem > 0)
        {
            src.Slice(first, rem).CopyTo(_buf.AsSpan(0, rem));
        }

        _write += n;

        // Keep Length <= Capacity by dropping oldest if necessary
        long maxLen = cap;
        long len = _write - _read;
        if (len > maxLen)
            _read = _write - maxLen;
    }

    /// <summary>Append a single sample.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteOne(float v)
    {
        _buf[(int)(_write & _mask)] = v;
        _write++;

        long maxLen = _buf.Length;
        long len = _write - _read;
        if (len > maxLen)
            _read = _write - maxLen;
    }

    /// <summary>
    /// In-place accumulation at the head (read pointer) offset by 'index' elements.
    /// Does not move read/write pointers. Used for overlap-add into pending region.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddAtFromHead(int index, float value)
    {
        // Caller guarantees 0 <= index < Length
#if DEBUG
        if ((uint)index >= (uint)Length)
            throw new ArgumentOutOfRangeException(
                nameof(index),
                "Index must be within pending length."
            );
#endif
        _buf[(int)((_read + index) & _mask)] += value;
    }

    /// <summary>
    /// Read a single element and advance. Returns 0 if called on empty buffer (debug builds throw).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadOne()
    {
#if DEBUG
        if (_write == _read)
            throw new InvalidOperationException("Ring is empty.");
#endif
        int r = (int)(_read & _mask);
        float v = _buf[r];
        _buf[r] = 0f; // clear to avoid re-use in accumulators
        _read++;
        return v;
    }

    /// <summary>
    /// Read interleaved frames from the ring into a planar buffer of shape [C][frames].
    /// If peekOnly=true, does not advance the read pointer.
    /// </summary>
    public void ReadInterleavedToPlanar(
        Span<float> dstPlanar,
        int channels,
        int frames,
        bool peekOnly
    )
    {
#if DEBUG
        int needed = channels * frames;
        if (needed > Length)
            throw new InvalidOperationException(
                $"Not enough data: need {needed}, have {Length}."
            );
#endif
        long start = _read;
        int cap = _buf.Length;

        // Copy sample-by-sample to planar layout [c * frames + i]
        for (int i = 0; i < frames; i++)
        {
            for (int c = 0; c < channels; c++)
            {
                int srcIdx = (int)((start + (long)(i * channels + c)) & _mask);
                dstPlanar[c * frames + i] = _buf[srcIdx];
            }
        }

        if (!peekOnly)
        {
            // Clear and advance by frames*channels in two contiguous blocks
            int count = channels * frames;
            AdvanceReadAndClear(count);
        }
    }

    /// <summary>
    /// Clears buffer and resets indices.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_buf, 0, _buf.Length);
        _write = 0;
        _read = 0;
    }

    public void Dispose()
    {
        // nothing to release
    }

    // ---- helpers ----

    /// <summary>
    /// Advance read pointer by 'count' elements, clearing the consumed region.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AdvanceReadAndClear(int count)
    {
        if (count <= 0)
            return;

        int cap = _buf.Length;
        int r = (int)(_read & _mask);

        int first = Math.Min(cap - r, count);
        if (first > 0)
            Array.Clear(_buf, r, first);

        int rem = count - first;
        if (rem > 0)
            Array.Clear(_buf, 0, rem);

        _read += count;
    }
}
