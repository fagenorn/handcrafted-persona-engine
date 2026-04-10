using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using PersonaEngine.Lib.IO;
using PersonaEngine.Lib.Utils.Onnx;

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

    // Persistent OrtValues wrapping pre-allocated input buffers.
    // Created once at construction, reused every Infer() call.
    // Safe because the underlying arrays are pre-allocated and never reallocated.
    private readonly OrtValue _windowOrt;
    private readonly OrtValue _identityOrt;
    private readonly OrtValue _emotionOrt;
    private readonly OrtValue _latentsOrt;
    private OrtValue? _noiseOrt;

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

        _session = OnnxSessionFactory.Create(
            modelPath,
            useGpu ? ExecutionProvider.CudaWithCpuFallback : ExecutionProvider.Cpu,
            SessionProfile.Sequential
        );

        _logger?.LogInformation("Audio2Face: ONNX session created from {ModelPath}.", modelPath);

        _windowOrt = OrtValue.CreateTensorValueFromMemory(
            OrtMemoryInfo.DefaultInstance,
            _windowBuffer.AsMemory(),
            [1, AudioBufferLen]
        );
        _identityOrt = OrtValue.CreateTensorValueFromMemory(
            OrtMemoryInfo.DefaultInstance,
            _identityBuffer.AsMemory(),
            [1, NumIdentities]
        );
        _emotionOrt = OrtValue.CreateTensorValueFromMemory(
            OrtMemoryInfo.DefaultInstance,
            _emotionBuffer.AsMemory(),
            [1, NumCenterFrames, NumEmotions]
        );
        _latentsOrt = OrtValue.CreateTensorValueFromMemory(
            OrtMemoryInfo.DefaultInstance,
            _latentsBuffer.AsMemory(),
            [NumDiffusionSteps, NumGruLayers, 1, GruLatentDim]
        );
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

        // 1. Update pre-allocated input buffers (OrtValues see changes via shared memory)
        Array.Clear(_windowBuffer);
        var copyLen = Math.Min(audio16Khz.Length, AudioBufferLen);
        audio16Khz[..copyLen].CopyTo(_windowBuffer);

        Array.Clear(_identityBuffer);
        _identityBuffer[identityIndex] = 1f;

        Array.Clear(_emotionBuffer);

        Array.Copy(_gruState, _latentsBuffer, _gruState.Length);

        EnsureNoiseInitialized();

        // 2. Bind inputs via IoBinding (no managed tensor wrappers)
        using var ioBinding = _session.CreateIoBinding();
        ioBinding.BindInput("window", _windowOrt);
        ioBinding.BindInput("identity", _identityOrt);
        ioBinding.BindInput("emotion", _emotionOrt);
        ioBinding.BindInput("input_latents", _latentsOrt);
        ioBinding.BindInput("noise", _noiseOrt!);

        // 3. Bind outputs to CPU (ONNX Runtime allocates result tensors)
        var cpuMem = OrtMemoryInfo.DefaultInstance;
        ioBinding.BindOutputToDevice("prediction", cpuMem);
        ioBinding.BindOutputToDevice("output_latents", cpuMem);

        // 4. Run inference
        using var runOptions = new RunOptions();
        using var results = _session.RunWithBoundResults(runOptions, ioBinding);

        // 5. Extract prediction via Span — replaces 2.16M indexed DenseTensor reads
        //    with 60 Span.Slice + CopyTo calls.
        //    prediction shape: [1, TotalFrames, TotalOutputDim]
        var predSpan = results[0].GetTensorDataAsSpan<float>();
        for (var frame = 0; frame < NumCenterFrames; frame++)
        {
            var srcOffset = (LeftPadFrames + frame) * TotalOutputDim;
            predSpan.Slice(srcOffset, SkinSize).CopyTo(_skinOutputBuffer.AsSpan(frame * SkinSize));
            predSpan
                .Slice(srcOffset + EyesOffset, EyesSize)
                .CopyTo(_eyeOutputBuffer.AsSpan(frame * EyesSize));
        }

        // 6. Extract GRU state — output_latents is [D, L, 1, H], contiguous and
        //    matches _gruState layout, so a single copy replaces the triple-nested loop.
        results[1].GetTensorDataAsSpan<float>().CopyTo(_gruState);

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

    private void EnsureNoiseInitialized()
    {
        if (_noiseOrt is not null)
        {
            return;
        }

        _cachedNoise = GenerateGaussianNoise(
            1 * (NumDiffusionSteps + 1) * TotalFrames * TotalOutputDim
        );
        _noiseOrt = OrtValue.CreateTensorValueFromMemory(
            OrtMemoryInfo.DefaultInstance,
            _cachedNoise.AsMemory(),
            [1, NumDiffusionSteps + 1, TotalFrames, TotalOutputDim]
        );
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _windowOrt.Dispose();
        _identityOrt.Dispose();
        _emotionOrt.Dispose();
        _latentsOrt.Dispose();
        _noiseOrt?.Dispose();
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
