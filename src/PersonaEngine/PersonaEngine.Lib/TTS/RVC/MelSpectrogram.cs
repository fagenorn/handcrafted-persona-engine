using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace PersonaEngine.Lib.TTS.RVC;

/// <summary>
///     Computes log-mel spectrograms matching the RMVPE reference implementation.
///     Parameters: sr=16000, n_fft=1024, hop=160, win=1024, 128 mels, fmin=30, fmax=8000, HTK scale.
/// </summary>
internal sealed class MelSpectrogram
{
    private const int SampleRate = 16000;
    private const int NFft = 1024;
    private const int HopLength = 160;
    private const int WinLength = 1024;
    private const int NMels = 128;
    private const float FMin = 30f;
    private const float FMax = 8000f;
    private const float LogClamp = 1e-5f;

    private const int NBins = NFft / 2 + 1; // 513

    private readonly float[] _hannWindow;
    private readonly float[,] _melFilterbank; // [NMels, NBins]

    public MelSpectrogram()
    {
        _hannWindow = CreateHannWindow(WinLength);
        _melFilterbank = CreateMelFilterbank();
    }

    /// <summary>
    ///     Computes a log-mel spectrogram from raw audio samples at 16kHz.
    ///     Returns a float array in [NMels, nFrames] layout for ONNX input.
    /// </summary>
    public (float[] Data, int Frames) Compute(ReadOnlySpan<float> audio)
    {
        var nFrames = audio.Length / HopLength + 1;

        // Center-pad the signal (reflect padding, pad = n_fft / 2)
        var pad = NFft / 2;
        var paddedLength = audio.Length + 2 * pad;
        var padded = new float[paddedLength];

        // Copy original signal with offset
        for (var i = 0; i < audio.Length; i++)
        {
            padded[pad + i] = audio[i];
        }

        // Reflect left edge
        for (var i = 0; i < pad; i++)
        {
            padded[pad - 1 - i] = audio[Math.Min(i + 1, audio.Length - 1)];
        }

        // Reflect right edge
        for (var i = 0; i < pad; i++)
        {
            var srcIdx = audio.Length - 2 - i;
            padded[pad + audio.Length + i] = audio[Math.Max(srcIdx, 0)];
        }

        // Compute STFT magnitude
        var magnitudes = new float[nFrames * NBins];
        var frame = new Complex[NFft];

        for (var t = 0; t < nFrames; t++)
        {
            var start = t * HopLength;

            // Apply Hann window and fill FFT frame
            for (var i = 0; i < NFft; i++)
            {
                var idx = start + i;
                var sample = idx < paddedLength ? padded[idx] : 0f;
                frame[i] = new Complex(sample * _hannWindow[i], 0);
            }

            Fourier.Forward(frame, FourierOptions.AsymmetricScaling);

            // Store magnitude of positive frequencies
            var baseIdx = t * NBins;
            for (var f = 0; f < NBins; f++)
            {
                magnitudes[baseIdx + f] = (float)frame[f].Magnitude;
            }
        }

        // Apply mel filterbank: mel[m, t] = sum_f(filterbank[m, f] * magnitude[t, f])
        var melSpec = new float[NMels * nFrames];
        for (var m = 0; m < NMels; m++)
        {
            for (var t = 0; t < nFrames; t++)
            {
                float sum = 0;
                var magBase = t * NBins;
                for (var f = 0; f < NBins; f++)
                {
                    sum += _melFilterbank[m, f] * magnitudes[magBase + f];
                }

                // Log with clamp: log(max(value, 1e-5))
                melSpec[m * nFrames + t] = MathF.Log(MathF.Max(sum, LogClamp));
            }
        }

        return (melSpec, nFrames);
    }

    private static float[] CreateHannWindow(int length)
    {
        var window = new float[length];
        for (var i = 0; i < length; i++)
        {
            window[i] = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / length);
        }

        return window;
    }

    /// <summary>
    ///     Creates a mel filterbank matching librosa.filters.mel(sr=16000, n_fft=1024, n_mels=128,
    ///     fmin=30, fmax=8000, htk=True).
    /// </summary>
    private static float[,] CreateMelFilterbank()
    {
        var filterbank = new float[NMels, NBins];

        // Create NMels+2 mel-spaced points
        var nPoints = NMels + 2;
        var melMin = HzToMelHtk(FMin);
        var melMax = HzToMelHtk(FMax);

        var melPoints = new float[nPoints];
        for (var i = 0; i < nPoints; i++)
        {
            melPoints[i] = melMin + i * (melMax - melMin) / (nPoints - 1);
        }

        // Convert mel points to Hz
        var melFreqs = new float[nPoints];
        for (var i = 0; i < nPoints; i++)
        {
            melFreqs[i] = MelToHzHtk(melPoints[i]);
        }

        // FFT bin center frequencies
        var fftFreqs = new float[NBins];
        for (var i = 0; i < NBins; i++)
        {
            fftFreqs[i] = (float)SampleRate * i / NFft;
        }

        // Build triangular filters with Slaney normalization
        for (var m = 0; m < NMels; m++)
        {
            var fLow = melFreqs[m];
            var fCenter = melFreqs[m + 1];
            var fHigh = melFreqs[m + 2];

            for (var f = 0; f < NBins; f++)
            {
                var freq = fftFreqs[f];

                if (freq >= fLow && freq <= fCenter && fCenter > fLow)
                {
                    filterbank[m, f] = (freq - fLow) / (fCenter - fLow);
                }
                else if (freq > fCenter && freq <= fHigh && fHigh > fCenter)
                {
                    filterbank[m, f] = (fHigh - freq) / (fHigh - fCenter);
                }
            }

            // Slaney normalization: 2 / (fHigh - fLow)
            var enorm = 2.0f / (fHigh - fLow);
            for (var f = 0; f < NBins; f++)
            {
                filterbank[m, f] *= enorm;
            }
        }

        return filterbank;
    }

    private static float HzToMelHtk(float hz)
    {
        return 2595.0f * MathF.Log10(1.0f + hz / 700.0f);
    }

    private static float MelToHzHtk(float mel)
    {
        return 700.0f * (MathF.Pow(10.0f, mel / 2595.0f) - 1.0f);
    }
}
