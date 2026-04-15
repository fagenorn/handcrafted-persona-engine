namespace PersonaEngine.Lib.Core.Conversation.Implementations.Adapters.Audio.Output;

/// <summary>
///     Pre-computed RMS amplitude envelope of an audio buffer, sampled in fixed-duration
///     windows. Lookups by elapsed time are O(1). Designed for reuse: owners hold one
///     instance for their lifetime and call <see cref="RebuildFrom" /> when a new source
///     buffer arrives — avoids the per-chunk allocation of the original static factory.
/// </summary>
internal sealed class AmplitudeEnvelope
{
    private float[] _windows = Array.Empty<float>();
    private int _windowCount;
    private float _windowDurationSeconds;

    public int WindowCount => _windowCount;

    public float WindowDurationSeconds => _windowDurationSeconds;

    /// <summary>Total audio duration the envelope covers.</summary>
    public float TotalDurationSeconds => _windowCount * _windowDurationSeconds;

    /// <summary>
    ///     Rebuilds the envelope in place from <paramref name="samples" />. Grows the
    ///     internal buffer via <see cref="Array.Resize{T}" /> only when additional capacity
    ///     is needed — steady-state operation allocates nothing.
    /// </summary>
    public void RebuildFrom(ReadOnlySpan<float> samples, int sampleRate, float windowMs = 30f)
    {
        if (samples.Length == 0 || sampleRate <= 0 || windowMs <= 0f)
        {
            Reset();
            return;
        }

        var windowSamples = Math.Max(1, (int)(sampleRate * windowMs / 1000f));
        var windowCount = (samples.Length + windowSamples - 1) / windowSamples;
        if (_windows.Length < windowCount)
            Array.Resize(ref _windows, windowCount);

        for (var w = 0; w < windowCount; w++)
        {
            var start = w * windowSamples;
            var end = Math.Min(start + windowSamples, samples.Length);
            var window = samples[start..end];

            var sum = 0f;
            for (var i = 0; i < window.Length; i++)
                sum += window[i] * window[i];

            _windows[w] = MathF.Sqrt(sum / window.Length);
        }

        _windowCount = windowCount;
        _windowDurationSeconds = windowSamples / (float)sampleRate;
    }

    /// <summary>Clears the envelope to empty (WindowCount == 0).</summary>
    public void Reset()
    {
        _windowCount = 0;
        _windowDurationSeconds = 0f;
    }

    /// <summary>
    ///     Returns the RMS amplitude of the window containing <paramref name="elapsedSeconds" />.
    ///     Clamped to the envelope's range; returns 0 for an empty envelope.
    /// </summary>
    public float SampleAt(float elapsedSeconds)
    {
        if (_windowCount == 0 || _windowDurationSeconds <= 0f)
            return 0f;

        var idx = (int)(elapsedSeconds / _windowDurationSeconds);
        idx = Math.Clamp(idx, 0, _windowCount - 1);
        return _windows[idx];
    }

    /// <summary>
    ///     Convenience factory that allocates a new instance and builds it from the given
    ///     samples. Kept for test and bench compatibility; production code should hold a
    ///     reusable instance and call <see cref="RebuildFrom" />.
    /// </summary>
    public static AmplitudeEnvelope From(
        ReadOnlySpan<float> samples,
        int sampleRate,
        float windowMs = 30f
    )
    {
        var env = new AmplitudeEnvelope();
        env.RebuildFrom(samples, sampleRate, windowMs);
        return env;
    }

    /// <summary>Shared empty envelope — behaves identically to a freshly constructed instance.</summary>
    public static readonly AmplitudeEnvelope Empty = new();
}
