using PersonaEngine.Lib.Utils;

namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Subscribes to <see cref="IMicrophone.SamplesAvailable" /> and computes a smoothed RMS
///     amplitude signal for UI meters. Writes one history entry per mic buffer event.
/// </summary>
public sealed class MicrophoneAmplitudeProvider : IMicrophoneAmplitudeProvider, IDisposable
{
    private const int HistoryCapacity = 64;
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

        // RMS = sqrt(mean(x^2))
        var sumSquares = 0f;
        for (var i = 0; i < samples.Length; i++)
            sumSquares += samples[i] * samples[i];
        var rms = MathF.Sqrt(sumSquares / samples.Length);
        if (rms > 1f)
            rms = 1f;

        // Exponential smoothing. dt is the buffer duration.
        var dt = samples.Length / (float)sampleRate;
        var alpha = 1f - MathF.Exp(-SmoothingRate * dt);
        _smoothed += (rms - _smoothed) * alpha;

        _history.Push(_smoothed);
    }
}
