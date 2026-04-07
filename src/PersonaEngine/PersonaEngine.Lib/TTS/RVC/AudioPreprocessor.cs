using Microsoft.ML.OnnxRuntime.Tensors;

namespace PersonaEngine.Lib.TTS.RVC;

/// <summary>
///     Pure signal-processing utilities for conditioning audio before RVC inference.
///     All methods operate in-place on float spans for zero-allocation processing.
/// </summary>
internal static class AudioPreprocessor
{
    /// <summary>
    ///     Applies a 5th-order high-pass Butterworth filter at 48 Hz (zero-phase,
    ///     forward-backward). Matches the official RVC Pipeline.py preprocessing.
    ///     Removes DC offset and sub-bass rumble that confuse HuBERT and F0 estimation.
    /// </summary>
    /// <remarks>
    ///     Coefficients precomputed for 16 kHz sample rate, 48 Hz cutoff, order 5.
    ///     Generated via scipy: <c>signal.butter(N=5, Wn=48, btype='high', fs=16000)</c>.
    /// </remarks>
    public static void ApplyHighPass48Hz(Span<float> audio)
    {
        if (audio.Length < 2)
        {
            return;
        }

        // 5th-order Butterworth high-pass, 48 Hz @ 16 kHz (second-order sections)
        // scipy.signal.butter(5, 48, 'high', fs=16000, output='sos')
        Span<Sos> sections =
        [
            new(
                b0: 0.99510512f,
                b1: -1.99021024f,
                b2: 0.99510512f,
                a1: -1.99020458f,
                a2: 0.99021590f
            ),
            new(
                b0: 0.99510512f,
                b1: -1.99021024f,
                b2: 0.99510512f,
                a1: -1.99052477f,
                a2: 0.99053614f
            ),
            new(b0: 0.99755128f, b1: -0.99755128f, b2: 0f, a1: -0.99510256f, a2: 0f),
        ];

        // Forward-backward filtering (zero-phase like scipy.signal.filtfilt)
        SosFilterInPlace(audio, sections);
        audio.Reverse();
        SosFilterInPlace(audio, sections);
        audio.Reverse();
    }

    /// <summary>
    ///     Peak-normalizes audio so max(|x|) == <paramref name="targetPeak" />.
    ///     Matches the official RVC normalization: <c>audio /= max(abs(audio)) / 0.95</c>.
    /// </summary>
    public static void PeakNormalize(Span<float> audio, float targetPeak = 0.95f)
    {
        var maxAbs = 0f;
        for (var i = 0; i < audio.Length; i++)
        {
            var abs = MathF.Abs(audio[i]);
            if (abs > maxAbs)
            {
                maxAbs = abs;
            }
        }

        if (maxAbs < 1e-8f)
        {
            return;
        }

        var scale = targetPeak / maxAbs;
        if (scale >= 1f)
        {
            return; // Already within range
        }

        for (var i = 0; i < audio.Length; i++)
        {
            audio[i] *= scale;
        }
    }

    /// <summary>
    ///     Applies a running median filter (kernel size 3) to the F0 contour.
    ///     Removes isolated spurious pitch spikes caused by noisy audio while
    ///     preserving genuine pitch transitions. Matches official RVC:
    ///     <c>signal.medfilt(f0, 3)</c>.
    /// </summary>
    public static void MedianFilterF0(Span<float> f0, int kernelSize = 3)
    {
        if (f0.Length < kernelSize)
        {
            return;
        }

        var halfK = kernelSize / 2;
        var prev = f0[0];

        for (var i = 1; i < f0.Length - 1; i++)
        {
            var left = prev;
            var center = f0[i];
            var right = f0[i + 1];

            prev = center;

            // Inline median of 3 — branch-free
            f0[i] = Median3(left, center, right);
        }
    }

    /// <summary>
    ///     Applies exponential moving average along the time axis of HuBERT features.
    ///     Acts as a temporal smoother, reducing frame-level noise from dirty audio.
    ///     <paramref name="alpha" /> = 1.0 means no smoothing; lower values smooth more.
    /// </summary>
    public static void SmoothHubertFeatures(DenseTensor<float> features, float alpha = 0.8f)
    {
        // features shape: [1, timeSteps, 768] (after transpose)
        var timeSteps = features.Dimensions[1];
        var channels = features.Dimensions[2];

        if (timeSteps < 2)
        {
            return;
        }

        // Forward EMA: feat[t] = alpha * feat[t] + (1 - alpha) * feat[t-1]
        var oneMinusAlpha = 1f - alpha;
        for (var t = 1; t < timeSteps; t++)
        {
            for (var c = 0; c < channels; c++)
            {
                features[0, t, c] =
                    alpha * features[0, t, c] + oneMinusAlpha * features[0, t - 1, c];
            }
        }
    }

    /// <summary>
    ///     Computes frame-level RMS energy of audio.
    /// </summary>
    public static float[] ComputeRmsEnvelope(
        ReadOnlySpan<float> audio,
        int frameLength,
        int hopLength
    )
    {
        var numFrames = Math.Max(1, (audio.Length - frameLength) / hopLength + 1);
        var rms = new float[numFrames];

        for (var i = 0; i < numFrames; i++)
        {
            var start = i * hopLength;
            var end = Math.Min(start + frameLength, audio.Length);
            var sum = 0f;
            for (var j = start; j < end; j++)
            {
                sum += audio[j] * audio[j];
            }

            rms[i] = MathF.Sqrt(sum / (end - start));
        }

        return rms;
    }

    /// <summary>
    ///     Applies RMS volume envelope matching from input to output audio.
    ///     Matches the official RVC <c>change_rms</c> function with
    ///     <c>rms_mix_rate=0</c> (fully match input envelope).
    /// </summary>
    public static void MatchRmsEnvelope(
        ReadOnlySpan<float> inputAudio,
        int inputSampleRate,
        Span<float> outputAudio,
        int outputSampleRate,
        float mixRate = 0.25f
    )
    {
        // Frame length = ~half second, hop = ~quarter second (matching official RVC)
        var inputFrameLen = inputSampleRate / 2;
        var inputHop = inputSampleRate / 2;
        var outputFrameLen = outputSampleRate / 2;
        var outputHop = outputSampleRate / 2;

        var inputRms = ComputeRmsEnvelope(inputAudio, inputFrameLen, inputHop);
        var outputRms = ComputeRmsEnvelope(outputAudio, outputFrameLen, outputHop);

        if (inputRms.Length == 0 || outputRms.Length == 0)
        {
            return;
        }

        // Apply per-sample gain: interpolate RMS envelopes to sample level
        for (var i = 0; i < outputAudio.Length; i++)
        {
            // Map sample index to fractional frame index
            var inputFrac = (float)i / outputAudio.Length * (inputRms.Length - 1);
            var outputFrac = (float)i / outputAudio.Length * (outputRms.Length - 1);

            var inRms = LerpArray(inputRms, inputFrac);
            var outRms = LerpArray(outputRms, outputFrac);

            if (outRms < 1e-6f)
            {
                continue;
            }

            // Blend: pow(inRms, 1-rate) * pow(outRms, rate-1)
            // At rate=0.25: mostly follows input envelope
            var gain = MathF.Pow(inRms, 1f - mixRate) * MathF.Pow(outRms, mixRate - 1f);

            // Clamp gain to avoid extreme amplification
            gain = Math.Clamp(gain, 0.1f, 10f);
            outputAudio[i] *= gain;
        }
    }

    private static float LerpArray(float[] arr, float index)
    {
        var lo = (int)index;
        var hi = Math.Min(lo + 1, arr.Length - 1);
        var frac = index - lo;
        return arr[lo] * (1f - frac) + arr[hi] * frac;
    }

    private static float Median3(float a, float b, float c)
    {
        if (a > b)
        {
            (a, b) = (b, a);
        }

        if (b > c)
        {
            (b, c) = (c, b);
        }

        if (a > b)
        {
            (a, b) = (b, a);
        }

        return b;
    }

    /// <summary>
    ///     Applies a cascade of second-order sections (SOS) IIR filter in-place.
    /// </summary>
    private static void SosFilterInPlace(Span<float> audio, Span<Sos> sections)
    {
        foreach (ref var sos in sections)
        {
            float w1 = 0,
                w2 = 0;
            for (var i = 0; i < audio.Length; i++)
            {
                var w0 = audio[i] - sos.A1 * w1 - sos.A2 * w2;
                audio[i] = sos.B0 * w0 + sos.B1 * w1 + sos.B2 * w2;
                w2 = w1;
                w1 = w0;
            }
        }
    }

    private readonly record struct Sos(float b0, float b1, float b2, float a1, float a2)
    {
        public float B0 => b0;
        public float B1 => b1;
        public float B2 => b2;
        public float A1 => a1;
        public float A2 => a2;
    }
}
