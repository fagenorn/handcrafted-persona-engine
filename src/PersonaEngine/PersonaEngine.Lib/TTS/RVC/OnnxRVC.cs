using System.Buffers;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PersonaEngine.Lib.TTS.RVC;

public class OnnxRVC : IDisposable
{
    /// <summary>
    ///     Reflection padding added to each side of the input before HuBERT and F0
    ///     estimation, then trimmed from the output. 0.5 s at 16 kHz = 8000 samples.
    ///     Mirrors the official RVC Pipeline.py t_pad approach.
    /// </summary>
    private const int ReflectionPadSamples = 8000;

    private readonly ArrayPool<float> _arrayPool;

    private readonly int _hopSize;

    private readonly InferenceSession _model;

    // Preallocated buffers and tensors
    private readonly DenseTensor<long> _speakerIdTensor;

    private readonly ContentVec _vecModel;

    public OnnxRVC(string modelPath, int hopsize, string vecPath)
    {
        var options = new SessionOptions
        {
            EnableMemoryPattern = true,
            ExecutionMode = ExecutionMode.ORT_PARALLEL,
            InterOpNumThreads = Environment.ProcessorCount,
            IntraOpNumThreads = Environment.ProcessorCount,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };

        options.AppendExecutionProvider_CUDA();

        _model = new InferenceSession(modelPath, options);
        _hopSize = hopsize;
        _vecModel = new ContentVec(vecPath);

        // Preallocate the speaker ID tensor
        _speakerIdTensor = new DenseTensor<long>(new[] { 1 });

        // Create array pool for temporary buffers
        _arrayPool = ArrayPool<float>.Shared;
    }

    public void Dispose()
    {
        _model?.Dispose();
        _vecModel?.Dispose();
    }

    public int ProcessAudio(
        ReadOnlyMemory<float> inputAudio,
        Memory<float> outputAudio,
        IF0Predictor f0Predictor,
        int speakerId,
        int f0UpKey
    )
    {
        // Early exit if input is empty
        if (inputAudio.Length == 0)
        {
            return 0;
        }

        // Check if output buffer is large enough
        if (outputAudio.Length < inputAudio.Length)
        {
            throw new ArgumentException("Output buffer is too small", nameof(outputAudio));
        }

        // Set the speaker ID
        _speakerIdTensor[0] = speakerId;

        // Process the audio
        return ProcessInPlace(inputAudio, outputAudio, f0Predictor, _speakerIdTensor, f0UpKey);
    }

    private int ProcessInPlace(
        ReadOnlyMemory<float> input,
        Memory<float> output,
        IF0Predictor f0Predictor,
        DenseTensor<long> speakerIdTensor,
        int f0UpKey
    )
    {
        const int f0Min = 50;
        const int f0Max = 1100;
        var f0MelMin = 1127 * Math.Log(1 + f0Min / 700.0);
        var f0MelMax = 1127 * Math.Log(1 + f0Max / 700.0);

        if (input.Length / 16000.0 > 30.0)
        {
            throw new Exception("Audio segment is too long (>30s)");
        }

        // --- Reflection-pad the input so HuBERT and F0 get full context at edges ---
        var padSamples = Math.Min(ReflectionPadSamples, input.Length);
        var paddedLen = padSamples + input.Length + padSamples;
        var paddedInput = _arrayPool.Rent(paddedLen);

        try
        {
            var inputSpan = input.Span;

            // Left reflection: mirror first padSamples of input
            for (var i = 0; i < padSamples; i++)
            {
                paddedInput[padSamples - 1 - i] = inputSpan[Math.Min(i, input.Length - 1)];
            }

            // Center: copy original input
            inputSpan.CopyTo(paddedInput.AsSpan(padSamples));

            // Right reflection: mirror last padSamples of input
            for (var i = 0; i < padSamples; i++)
            {
                paddedInput[padSamples + input.Length + i] = inputSpan[
                    Math.Max(input.Length - 1 - i, 0)
                ];
            }

            var paddedSpan = paddedInput.AsSpan(0, paddedLen);

            // --- Audio conditioning (matches official RVC Pipeline.py) ---
            // 1. High-pass Butterworth at 48 Hz — removes DC offset and sub-bass rumble
            AudioPreprocessor.ApplyHighPass48Hz(paddedSpan);

            // 2. Peak-normalize to 0.95 — ensures consistent input level for models
            AudioPreprocessor.PeakNormalize(paddedSpan);

            var paddedMemory = new ReadOnlyMemory<float>(paddedInput, 0, paddedLen);

            // Get the HuBERT features on conditioned, padded input
            var hubert = _vecModel.Forward(paddedMemory);

            // Repeat and transpose the features
            var hubertRepeated = RVCUtils.RepeatTensor(hubert, 2);
            hubertRepeated = RVCUtils.Transpose(hubertRepeated, 0, 2, 1);

            hubert = null; // Allow for garbage collection

            // 3. Temporal smoothing on HuBERT features — reduces frame-level noise
            AudioPreprocessor.SmoothHubertFeatures(hubertRepeated, alpha: 0.8f);

            var hubertLength = hubertRepeated.Dimensions[1];
            var hubertLengthTensor = new DenseTensor<long>([1]) { [0] = hubertLength };

            // Allocate buffers for F0 calculations
            var f0Buffer = _arrayPool.Rent(hubertLength);
            var f0Memory = new Memory<float>(f0Buffer, 0, hubertLength);

            try
            {
                // Calculate F0 on conditioned, padded input
                f0Predictor.ComputeF0(paddedMemory, f0Memory, hubertLength);

                // 4. Median filter F0 — removes spurious pitch spikes from noisy audio
                AudioPreprocessor.MedianFilterF0(f0Buffer.AsSpan(0, hubertLength), kernelSize: 3);

                // Create pitch tensors
                var pitchBuffer = _arrayPool.Rent(hubertLength);
                var pitchTensor = new DenseTensor<long>(new[] { 1, hubertLength });
                var pitchfTensor = new DenseTensor<float>(new[] { 1, hubertLength });

                try
                {
                    // Apply pitch shift and convert to mel scale
                    for (var i = 0; i < hubertLength; i++)
                    {
                        // Apply pitch shift
                        var shiftedF0 = f0Buffer[i] * (float)Math.Pow(2, f0UpKey / 12.0);
                        pitchfTensor[0, i] = shiftedF0;

                        // Convert to mel scale for pitch
                        var f0Mel = 1127 * Math.Log(1 + shiftedF0 / 700.0);
                        if (f0Mel > 0)
                        {
                            f0Mel = (f0Mel - f0MelMin) * 254 / (f0MelMax - f0MelMin) + 1;
                            f0Mel = Math.Round(f0Mel);
                        }

                        if (f0Mel <= 1)
                        {
                            f0Mel = 1;
                        }

                        if (f0Mel > 255)
                        {
                            f0Mel = 255;
                        }

                        pitchTensor[0, i] = (long)f0Mel;
                    }

                    // Generate random noise tensor — must be Gaussian N(0,1)
                    // to match torch.randn() in the official RVC pipeline
                    var rndTensor = new DenseTensor<float>(new[] { 1, 192, hubertLength });
                    var random = new Random();
                    for (var i = 0; i < 192 * hubertLength; i++)
                    {
                        // Box-Muller transform: uniform [0,1) → standard normal N(0,1)
                        var u1 = 1.0 - random.NextDouble(); // (0, 1] to avoid log(0)
                        var u2 = random.NextDouble();
                        var gaussian = (float)(
                            Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2)
                        );
                        rndTensor[0, i / hubertLength, i % hubertLength] = gaussian;
                    }

                    // Run the model
                    var outWav = Forward(
                        hubertRepeated,
                        hubertLengthTensor,
                        pitchTensor,
                        pitchfTensor,
                        speakerIdTensor,
                        rndTensor
                    );

                    // --- Trim reflection-padded edges from output ---
                    var trimOutputSamples = (int)
                        Math.Round((double)padSamples / paddedLen * outWav.Length);
                    var trimmedStart = trimOutputSamples;
                    var trimmedEnd = outWav.Length - trimOutputSamples;

                    if (trimmedEnd <= trimmedStart)
                    {
                        trimmedStart = 0;
                        trimmedEnd = outWav.Length;
                    }

                    var trimmedLength = trimmedEnd - trimmedStart;

                    // Convert short → float directly (no per-chunk normalization)
                    var outputSpan = output.Span;
                    if (outputSpan.Length < trimmedLength)
                    {
                        throw new InvalidOperationException(
                            $"Output buffer too small. Needed {trimmedLength}, but only had {outputSpan.Length}"
                        );
                    }

                    const float shortToFloat = 1.0f / 32768f;
                    for (var i = 0; i < trimmedLength; i++)
                    {
                        outputSpan[i] = outWav[trimmedStart + i] * shortToFloat;
                    }

                    // 5. RMS envelope matching — prevent volume fluctuations from RVC
                    AudioPreprocessor.MatchRmsEnvelope(
                        inputAudio: inputSpan,
                        inputSampleRate: 16000,
                        outputAudio: outputSpan[..trimmedLength],
                        outputSampleRate: 16000,
                        mixRate: 0.25f
                    );

                    return trimmedLength;
                }
                finally
                {
                    _arrayPool.Return(pitchBuffer);
                }
            }
            finally
            {
                _arrayPool.Return(f0Buffer);
            }
        }
        finally
        {
            _arrayPool.Return(paddedInput);
        }
    }

    private short[] Forward(
        DenseTensor<float> hubert,
        DenseTensor<long> hubertLength,
        DenseTensor<long> pitch,
        DenseTensor<float> pitchf,
        DenseTensor<long> speakerId,
        DenseTensor<float> noise
    )
    {
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_model.InputMetadata.Keys.ElementAt(0), hubert),
            NamedOnnxValue.CreateFromTensor(_model.InputMetadata.Keys.ElementAt(1), hubertLength),
            NamedOnnxValue.CreateFromTensor(_model.InputMetadata.Keys.ElementAt(2), pitch),
            NamedOnnxValue.CreateFromTensor(_model.InputMetadata.Keys.ElementAt(3), pitchf),
            NamedOnnxValue.CreateFromTensor(_model.InputMetadata.Keys.ElementAt(4), speakerId),
            NamedOnnxValue.CreateFromTensor(_model.InputMetadata.Keys.ElementAt(5), noise),
        };

        var results = _model.Run(inputs);
        var output = results.First().AsTensor<float>();

        return output.Select(x => (short)(x * 32767)).ToArray();
    }
}
