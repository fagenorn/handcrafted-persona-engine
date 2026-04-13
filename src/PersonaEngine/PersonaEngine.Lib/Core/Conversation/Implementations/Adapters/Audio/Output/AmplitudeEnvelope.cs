namespace PersonaEngine.Lib.Core.Conversation.Implementations.Adapters.Audio.Output;

/// <summary>
/// Pre-computed RMS amplitude envelope of an audio buffer, sampled in fixed
/// time windows. Lookups by elapsed time are O(1).
/// </summary>
internal sealed class AmplitudeEnvelope
{
    private readonly float[] _windows;
    private readonly float _windowDurationSeconds;

    public static readonly AmplitudeEnvelope Empty = new(Array.Empty<float>(), 0.03f);

    private AmplitudeEnvelope(float[] windows, float windowDurationSeconds)
    {
        _windows = windows;
        _windowDurationSeconds = windowDurationSeconds;
    }

    public int WindowCount => _windows.Length;

    public float WindowDurationSeconds => _windowDurationSeconds;

    /// <summary>Total audio duration the envelope covers.</summary>
    public float TotalDurationSeconds => _windows.Length * _windowDurationSeconds;

    /// <summary>
    /// Builds an envelope by computing RMS amplitude in fixed-duration windows
    /// across the input samples. Default window is 30 ms, which gives ~33 Hz
    /// envelope sampling — fine enough for per-syllable voice visualization.
    /// </summary>
    public static AmplitudeEnvelope From(
        ReadOnlySpan<float> samples,
        int sampleRate,
        float windowMs = 30f
    )
    {
        if (samples.Length == 0 || sampleRate <= 0 || windowMs <= 0f)
            return Empty;

        var windowSamples = Math.Max(1, (int)(sampleRate * windowMs / 1000f));
        var windowCount = (samples.Length + windowSamples - 1) / windowSamples;
        var windows = new float[windowCount];

        for (var w = 0; w < windowCount; w++)
        {
            var start = w * windowSamples;
            var end = Math.Min(start + windowSamples, samples.Length);
            var window = samples[start..end];

            var sum = 0f;
            for (var i = 0; i < window.Length; i++)
                sum += window[i] * window[i];

            windows[w] = MathF.Sqrt(sum / window.Length);
        }

        return new AmplitudeEnvelope(windows, windowSamples / (float)sampleRate);
    }

    /// <summary>
    /// Returns the RMS amplitude of the window containing <paramref name="elapsedSeconds"/>.
    /// Clamped to the envelope's range; returns 0 for an empty envelope.
    /// </summary>
    public float SampleAt(float elapsedSeconds)
    {
        if (_windows.Length == 0)
            return 0f;

        var idx = (int)(elapsedSeconds / _windowDurationSeconds);
        idx = Math.Clamp(idx, 0, _windows.Length - 1);
        return _windows[idx];
    }
}
