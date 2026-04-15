using PersonaEngine.Lib.Utils;

namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Subscribes to <see cref="IMicrophone.SamplesAvailable" /> and computes a smoothed RMS
///     amplitude signal for UI meters.
///     <para>
///         Each incoming mic buffer is split into <see cref="SubWindowsPerBuffer" /> sub-windows;
///         one history entry is emitted per sub-window. NAudio's default buffer is 100 ms, so a
///         single raw event would only advance the sparkline at ~10 Hz — too slow to feel fluid.
///         Sub-sampling lifts the effective update rate to ~160 Hz (matching display refresh)
///         without changing the algorithm: each sub-window is its own RMS + smoothing step.
///     </para>
/// </summary>
public sealed class MicrophoneAmplitudeProvider : IMicrophoneAmplitudeProvider, IDisposable
{
    private const int HistoryCapacity = 128;
    private const int SubWindowsPerBuffer = 16;
    private const int MinSamplesPerWindow = 100; // ~6.25 ms at 16 kHz; prevents degenerate tiny windows
    private const float SmoothingRate = 12f; // matches WindowFrameGlow / StatusBar convention

    private readonly IMicrophone _microphone;
    private FloatRingBuffer _history = new(HistoryCapacity);
    private float _smoothed;

    public MicrophoneAmplitudeProvider(IMicrophone microphone)
    {
        _microphone = microphone;
        _microphone.SamplesAvailable += OnSamplesAvailable;
    }

    public float CurrentAmplitude => _smoothed;

    public ReadOnlySpan<float> History => _history.Values;

    public int HistoryHead => _history.Head;

    public void Dispose() => _microphone.SamplesAvailable -= OnSamplesAvailable;

    private void OnSamplesAvailable(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (samples.IsEmpty || sampleRate <= 0)
            return;

        // Split the buffer into sub-windows. If the buffer is smaller than the nominal
        // sub-window size (e.g. a very short capture), process it as a single window —
        // still emit one entry so the meter keeps animating even on short buffers.
        // MinSamplesPerWindow prevents degenerate 1-2 sample windows on tiny buffers.
        var windowCount = Math.Clamp(
            samples.Length / Math.Max(1, MinSamplesPerWindow),
            1,
            SubWindowsPerBuffer
        );

        var windowLength = samples.Length / windowCount;
        var remainder = samples.Length - (windowLength * windowCount);

        var start = 0;
        for (var w = 0; w < windowCount; w++)
        {
            // Distribute any remainder across the first few windows so we cover every sample.
            var length = windowLength + (w < remainder ? 1 : 0);
            var window = samples.Slice(start, length);
            start += length;

            PushWindowSample(window, sampleRate);
        }
    }

    private void PushWindowSample(ReadOnlySpan<float> window, int sampleRate)
    {
        // RMS = sqrt(mean(x^2)). Clamp to 1.0 as a defensive ceiling.
        var sumSquares = 0f;
        for (var i = 0; i < window.Length; i++)
            sumSquares += window[i] * window[i];
        var rms = MathF.Sqrt(sumSquares / window.Length);
        if (rms > 1f)
            rms = 1f;

        // Exponential smoothing — dt is this sub-window's duration.
        var dt = window.Length / (float)sampleRate;
        var alpha = 1f - MathF.Exp(-SmoothingRate * dt);
        _smoothed += (rms - _smoothed) * alpha;

        _history.Push(_smoothed);
    }
}
