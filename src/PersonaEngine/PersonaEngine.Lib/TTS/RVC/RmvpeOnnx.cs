using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PersonaEngine.Lib.TTS.RVC;

/// <summary>
///     RMVPE (Robust Model for Vocal Pitch Estimation) F0 predictor.
///     Uses a mel-spectrogram input ONNX model with local-average decoding.
///     Better suited for expressive speech than CREPE, as it handles rapid pitch changes natively.
/// </summary>
public class RmvpeOnnx : IF0Predictor
{
    private const int PitchBins = 360;
    private const int NMels = 128;
    private const int PadMultiple = 32;
    private const float CentsConst = 1997.3794084376191f;
    private const float CentsPerBin = 20f;
    private const int LocalAvgRadius = 4; // 9-bin window centered on argmax
    private const float DefaultThreshold = 0.03f;

    private readonly MelSpectrogram _melSpectrogram;
    private readonly InferenceSession _session;
    private readonly float _threshold;

    public RmvpeOnnx(string modelPath, float threshold = DefaultThreshold)
    {
        _threshold = threshold;

        var options = new SessionOptions
        {
            EnableMemoryPattern = true,
            ExecutionMode = ExecutionMode.ORT_PARALLEL,
            InterOpNumThreads = Environment.ProcessorCount,
            IntraOpNumThreads = Environment.ProcessorCount,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_FATAL,
        };

        options.AppendExecutionProvider_CPU();

        _session = new InferenceSession(modelPath, options);
        _melSpectrogram = new MelSpectrogram();
    }

    public void ComputeF0(ReadOnlyMemory<float> wav, Memory<float> f0Output, int length)
    {
        if (length > f0Output.Length)
        {
            throw new ArgumentException("Output buffer is too small", nameof(f0Output));
        }

        var wavSpan = wav.Span;

        // Step 1: Compute mel spectrogram [NMels, nFrames]
        var (melData, nFrames) = _melSpectrogram.Compute(wavSpan);

        // Step 2: Pad time axis to multiple of 32 (reflect padding)
        var paddedFrames = PadMultiple * ((nFrames - 1) / PadMultiple + 1);

        // Step 3: Create input tensor [1, NMels, paddedFrames]
        var inputTensor = new DenseTensor<float>(new[] { 1, NMels, paddedFrames });

        for (var m = 0; m < NMels; m++)
        {
            var srcBase = m * nFrames;

            // Copy original frames
            for (var t = 0; t < nFrames; t++)
            {
                inputTensor[0, m, t] = melData[srcBase + t];
            }

            // Reflect-pad remaining frames
            for (var t = nFrames; t < paddedFrames; t++)
            {
                var reflectIdx = nFrames - 1 - (t - nFrames);
                reflectIdx = Math.Clamp(reflectIdx, 0, nFrames - 1);
                inputTensor[0, m, t] = melData[srcBase + reflectIdx];
            }
        }

        // Step 4: Run inference
        var inputName = _session.InputMetadata.Keys.First();
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputName, inputTensor),
        };

        using var results = _session.Run(inputs);
        var output = results.First().AsTensor<float>();

        // Output shape: [1, paddedFrames, 360] - decode only up to nFrames
        var f0Span = f0Output.Span;
        var framesToDecode = Math.Min(nFrames, length);

        for (var t = 0; t < framesToDecode; t++)
        {
            // Find argmax and max salience for this frame
            var maxBin = 0;
            var maxSalience = 0f;
            for (var b = 0; b < PitchBins; b++)
            {
                var val = output[0, t, b];
                if (val > maxSalience)
                {
                    maxSalience = val;
                    maxBin = b;
                }
            }

            // Voicing decision
            if (maxSalience <= _threshold)
            {
                f0Span[t] = 0f;
                continue;
            }

            // Local average: weighted average of 9 bins centered on argmax
            float weightedSum = 0;
            float weightSum = 0;

            for (var offset = -LocalAvgRadius; offset <= LocalAvgRadius; offset++)
            {
                var bin = maxBin + offset;
                if (bin < 0 || bin >= PitchBins)
                    continue;

                var salience = output[0, t, bin];
                var cents = CentsPerBin * bin + CentsConst;
                weightedSum += salience * cents;
                weightSum += salience;
            }

            var avgCents = weightSum > 0 ? weightedSum / weightSum : 0f;

            // Convert cents to Hz: f0 = 10 * 2^(cents / 1200)
            var f0Hz = 10f * MathF.Pow(2f, avgCents / 1200f);

            // Guard against the unvoiced marker frequency (10 Hz when cents=0)
            f0Span[t] = f0Hz <= 10f ? 0f : f0Hz;
        }

        // Zero-fill any remaining frames if length > nFrames
        for (var t = framesToDecode; t < length; t++)
        {
            f0Span[t] = 0f;
        }
    }

    public void Dispose()
    {
        _session.Dispose();
    }
}
