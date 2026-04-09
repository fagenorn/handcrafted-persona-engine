using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PersonaEngine.Lib.IO;

namespace PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;

/// <summary>
///     Wraps the Audio2Face-3D v3.0 ONNX model, managing the inference session,
///     input/output tensors, and recurrent GRU state across invocations.
/// </summary>
public sealed class Audio2FaceInference : IDisposable
{
    /// <summary>Audio buffer length: 1 second at 16 kHz.</summary>
    private const int AudioBufferLen = 16000;

    /// <summary>Number of emotion dimensions in the input.</summary>
    private const int NumEmotions = 10;

    /// <summary>Number of center frames extracted from the prediction.</summary>
    private const int NumCenterFrames = 30;

    /// <summary>Number of identity slots (one-hot encoding dimension).</summary>
    private const int NumIdentities = 3;

    /// <summary>Number of diffusion denoising steps.</summary>
    private const int NumDiffusionSteps = 2;

    /// <summary>Number of GRU layers in the network.</summary>
    private const int NumGruLayers = 2;

    /// <summary>Hidden dimension of each GRU layer.</summary>
    private const int GruLatentDim = 256;

    /// <summary>Total frames per inference window (15 left + 30 center + 15 right).</summary>
    private const int TotalFrames = 60;

    /// <summary>Total output dimension per frame from the network prediction.</summary>
    private const int TotalOutputDim = 88831;

    /// <summary>Skin vertex data size per frame (24002 vertices * 3 components).</summary>
    private const int SkinSize = 72006;

    /// <summary>Eye rotation data: [rightX, rightY, leftX, leftY] per frame.</summary>
    private const int EyesSize = 4;

    /// <summary>Offset within prediction where eye rotation starts (after skin + tongue + jaw).</summary>
    private const int EyesOffset = 72006 + 16806 + 15; // skin + tongue + jaw

    /// <summary>Number of frames to skip at the start of the prediction (left padding).</summary>
    private const int LeftPadFrames = 15;

    /// <summary>Fixed seed for reproducible Gaussian noise generation.</summary>
    private const int NoiseSeed = 0;

    private readonly ILogger? _logger;

    private readonly InferenceSession _session;

    /// <summary>
    ///     Recurrent GRU hidden state carried across inference calls.
    ///     Shape: [NumDiffusionSteps, NumGruLayers, 1, GruLatentDim].
    /// </summary>
    private readonly float[] _gruState;

    // Pre-allocated buffers reused across Infer() calls
    private readonly float[] _windowBuffer = new float[AudioBufferLen];
    private readonly float[] _identityBuffer = new float[NumIdentities];
    private readonly float[] _emotionBuffer = new float[1 * NumCenterFrames * NumEmotions];
    private readonly float[] _latentsBuffer;
    private readonly float[] _skinOutputBuffer = new float[NumCenterFrames * SkinSize];
    private readonly float[] _eyeOutputBuffer = new float[NumCenterFrames * EyesSize];

    private float[]? _cachedNoise;

    private bool _disposed;

    /// <summary>
    ///     Initializes the Audio2Face inference wrapper.
    /// </summary>
    /// <param name="modelProvider">Provider to resolve model file paths.</param>
    /// <param name="useGpu">
    ///     Whether to attempt CUDA execution. Falls back to CPU on failure.
    /// </param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public Audio2FaceInference(IModelProvider modelProvider, bool useGpu, ILogger? logger = null)
    {
        _logger = logger;
        _gruState = new float[NumDiffusionSteps * NumGruLayers * 1 * GruLatentDim];
        _latentsBuffer = new float[NumDiffusionSteps * NumGruLayers * 1 * GruLatentDim];

        var modelPath = modelProvider.GetModelPath(IO.ModelType.Audio2Face.Network);

        var sessionOptions = new SessionOptions
        {
            EnableMemoryPattern = true,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };

        if (useGpu)
        {
            try
            {
                sessionOptions.AppendExecutionProvider_CUDA();
                _logger?.LogInformation("Audio2Face: CUDA execution provider enabled.");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Audio2Face: Failed to initialize CUDA, falling back to CPU."
                );
            }
        }

        try
        {
            _session = new InferenceSession(modelPath, sessionOptions);
            _logger?.LogInformation(
                "Audio2Face: ONNX session created from {ModelPath}.",
                modelPath
            );
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Audio2Face: Failed to create ONNX inference session.");
            sessionOptions.Dispose();

            throw;
        }

        sessionOptions.Dispose();
    }

    /// <summary>
    ///     Runs a single inference pass, producing 30 center frames of skin vertex
    ///     data and eye rotation angles.
    /// </summary>
    /// <param name="audio16Khz">
    ///     Audio samples at 16 kHz. If shorter than <see cref="AudioBufferLen" />,
    ///     the remainder is zero-padded. If longer, only the first
    ///     <see cref="AudioBufferLen" /> samples are used.
    /// </param>
    /// <param name="identityIndex">
    ///     Identity slot index (0-based, must be less than <see cref="NumIdentities" />).
    /// </param>
    /// <returns>
    ///     Flat skin vertices [30 * 72006] and flat eye rotation [30 * 4] (rightX, rightY, leftX, leftY),
    ///     plus the frame count.  The returned arrays are internal buffers — callers must not hold
    ///     references across calls.
    /// </returns>
    public (float[] SkinFlat, float[] EyeFlat, int FrameCount) Infer(
        ReadOnlySpan<float> audio16Khz,
        int identityIndex = 0
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if ((uint)identityIndex >= NumIdentities)
        {
            throw new ArgumentOutOfRangeException(
                nameof(identityIndex),
                identityIndex,
                $"Identity index must be in [0, {NumIdentities})."
            );
        }

        // 1. Build window tensor [1, AudioBufferLen]
        Array.Clear(_windowBuffer);
        var copyLen = Math.Min(audio16Khz.Length, AudioBufferLen);
        audio16Khz[..copyLen].CopyTo(_windowBuffer);
        var windowTensor = new DenseTensor<float>(_windowBuffer, [1, AudioBufferLen]);

        // 2. Build identity tensor [1, NumIdentities] — one-hot
        Array.Clear(_identityBuffer);
        _identityBuffer[identityIndex] = 1f;
        var identityTensor = new DenseTensor<float>(_identityBuffer, [1, NumIdentities]);

        // 3. Build emotion tensor [1, NumCenterFrames, NumEmotions] — all zeros (neutral)
        Array.Clear(_emotionBuffer);
        var emotionTensor = new DenseTensor<float>(
            _emotionBuffer,
            [1, NumCenterFrames, NumEmotions]
        );

        // 4. Build input_latents tensor [NumDiffusionSteps, NumGruLayers, 1, GruLatentDim] — copy from GRU state
        Array.Copy(_gruState, _latentsBuffer, _gruState.Length);
        var latentsTensor = new DenseTensor<float>(
            _latentsBuffer,
            [NumDiffusionSteps, NumGruLayers, 1, GruLatentDim]
        );

        // 5. Build noise tensor [1, NumDiffusionSteps + 1, TotalFrames, TotalOutputDim] — Gaussian via Box-Muller, cached for session lifetime
        _cachedNoise ??= GenerateGaussianNoise(
            1 * (NumDiffusionSteps + 1) * TotalFrames * TotalOutputDim
        );
        var noiseTensor = new DenseTensor<float>(
            _cachedNoise,
            [1, NumDiffusionSteps + 1, TotalFrames, TotalOutputDim]
        );

        var inputs = new List<NamedOnnxValue>(5)
        {
            NamedOnnxValue.CreateFromTensor("window", windowTensor),
            NamedOnnxValue.CreateFromTensor("identity", identityTensor),
            NamedOnnxValue.CreateFromTensor("emotion", emotionTensor),
            NamedOnnxValue.CreateFromTensor("input_latents", latentsTensor),
            NamedOnnxValue.CreateFromTensor("noise", noiseTensor),
        };

        using var results = _session.Run(inputs);

        // Extract prediction and output_latents in a single pass over results
        Tensor<float>? predictionTensor = null;
        Tensor<float>? outputLatentsTensor = null;

        foreach (var result in results)
        {
            switch (result.Name)
            {
                case "prediction":
                    predictionTensor = result.AsTensor<float>();
                    break;
                case "output_latents":
                    outputLatentsTensor = result.AsTensor<float>();
                    break;
            }

            if (predictionTensor != null && outputLatentsTensor != null)
            {
                break;
            }
        }

        if (predictionTensor == null || outputLatentsTensor == null)
        {
            throw new InvalidOperationException(
                "ONNX model did not return expected 'prediction' and 'output_latents' outputs."
            );
        }

        // Extract prediction: skip LeftPadFrames, take NumCenterFrames into flat buffers
        Array.Clear(_skinOutputBuffer);
        Array.Clear(_eyeOutputBuffer);

        for (var frame = 0; frame < NumCenterFrames; frame++)
        {
            var sourceFrame = LeftPadFrames + frame;
            var skinBase = frame * SkinSize;
            var eyeBase = frame * EyesSize;

            for (var j = 0; j < SkinSize; j++)
            {
                _skinOutputBuffer[skinBase + j] = predictionTensor[0, sourceFrame, j];
            }

            for (var j = 0; j < EyesSize; j++)
            {
                _eyeOutputBuffer[eyeBase + j] = predictionTensor[0, sourceFrame, EyesOffset + j];
            }
        }

        // Extract output_latents and copy to GRU state
        var gruIdx = 0;

        for (var d = 0; d < NumDiffusionSteps; d++)
        {
            for (var l = 0; l < NumGruLayers; l++)
            {
                for (var h = 0; h < GruLatentDim; h++)
                {
                    _gruState[gruIdx++] = outputLatentsTensor[d, l, 0, h];
                }
            }
        }

        return (_skinOutputBuffer, _eyeOutputBuffer, NumCenterFrames);
    }

    /// <summary>
    ///     Resets the GRU hidden state to zeros, clearing any temporal context
    ///     accumulated from previous inference calls.
    /// </summary>
    public void ResetState()
    {
        Array.Clear(_gruState);
    }

    /// <summary>
    ///     Returns a copy of the current GRU hidden state.
    /// </summary>
    public float[] SaveGruState()
    {
        var saved = new float[_gruState.Length];
        Array.Copy(_gruState, saved, _gruState.Length);
        return saved;
    }

    /// <summary>
    ///     Overwrites the GRU hidden state from a previously saved copy.
    /// </summary>
    public void RestoreGruState(float[] saved)
    {
        Array.Copy(saved, _gruState, _gruState.Length);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Dispose();
        _cachedNoise = null;
    }

    /// <summary>
    ///     Generates Gaussian-distributed noise using the Box-Muller transform
    ///     with a deterministic seed for reproducibility.
    /// </summary>
    private static float[] GenerateGaussianNoise(int count)
    {
        var rng = new Random(NoiseSeed);
        var noise = new float[count];

        for (var i = 0; i < count - 1; i += 2)
        {
            var u1 = 1.0 - rng.NextDouble();
            var u2 = rng.NextDouble();
            var magnitude = Math.Sqrt(-2.0 * Math.Log(u1));
            var angle = 2.0 * Math.PI * u2;

            noise[i] = (float)(magnitude * Math.Cos(angle));
            noise[i + 1] = (float)(magnitude * Math.Sin(angle));
        }

        // Handle odd count
        if (count % 2 != 0)
        {
            var u1 = 1.0 - rng.NextDouble();
            var u2 = rng.NextDouble();
            noise[count - 1] = (float)(
                Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2)
            );
        }

        return noise;
    }
}
