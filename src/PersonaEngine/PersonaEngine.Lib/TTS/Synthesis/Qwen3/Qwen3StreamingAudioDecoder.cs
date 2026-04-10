using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using PersonaEngine.Lib.Utils.Pooling;

namespace PersonaEngine.Lib.TTS.Synthesis.Qwen3;

/// <summary>
///     Stateful streaming audio decoder for Qwen3-TTS (qwen3_tts_decoder.onnx).
///     Maintains KV-cache (8 transformer layers) and convolutional history buffers
///     across calls for true incremental decoding — no redundant re-computation.
///     State tensors are trimmed after each decode to match LunaVox window sizes,
///     preventing unbounded growth during long utterances.
/// </summary>
public sealed class Qwen3StreamingAudioDecoder : IDisposable
{
    // Decoder architecture constants matching qwen3_tts_decoder.onnx
    private const int NumKvLayers = 8;
    private const int NumHeads = 16;
    private const int HeadDim = 64;
    private const int PreConvChannels = 512;
    private const int LatentChannels = 1024;
    private const int ConvChannels = 1024;
    private const int NumCodeGroups = 16;

    // State trimming windows (matching LunaVox StatefulDecoderOnnx defaults)
    private const int PreConvWindow = 2;
    private const int LatentWindow = 4;
    private const int ConvWindow = 4;
    private const int KvCacheWindow = 72;

    private readonly InferenceSession _session;
    private readonly ILogger? _logger;

    // Persistent state — always owned by us (trimmed copies after each decode)
    private OrtValue _preConvHistory = null!;
    private OrtValue _latentBuffer = null!;
    private OrtValue _convHistory = null!;
    private readonly OrtValue[] _pastKeys = new OrtValue[NumKvLayers];
    private readonly OrtValue[] _pastValues = new OrtValue[NumKvLayers];

    private bool _disposed;
    private int _decodeCallCount;

    public Qwen3StreamingAudioDecoder(InferenceSession session, ILogger? logger = null)
    {
        _session = session;
        _logger = logger;
        InitializeState();
    }

    /// <summary>
    ///     Decodes codec frames to audio, updating internal state for the next call.
    ///     State tensors are trimmed to bounded windows after each call.
    /// </summary>
    /// <param name="frames">New codec frames. Each element is int[16] (one per code group).</param>
    /// <param name="isLast">Set true on the final call to flush buffered audio.</param>
    /// <returns>PCM float32 audio at 24 kHz, clamped to [-1, 1].</returns>
    public float[] Decode(List<int[]> frames, bool isLast)
    {
        var numFrames = frames.Count;
        var codesLen = numFrames * NumCodeGroups;

        // Build audio_codes [1, numFrames, 16] as int64
        using var codesBuffer = PooledArray<long>.Rent(Math.Max(codesLen, 1));

        {
            for (var f = 0; f < numFrames; f++)
            {
                var frame = frames[f];
                for (var g = 0; g < NumCodeGroups; g++)
                {
                    codesBuffer.Array[f * NumCodeGroups + g] = Math.Clamp(frame[g], 0, 2047);
                }
            }

            var codesMemory =
                codesLen > 0 ? codesBuffer.Array.AsMemory(0, codesLen) : Memory<long>.Empty;

            using var codesOrt = OrtValue.CreateTensorValueFromMemory(
                OrtMemoryInfo.DefaultInstance,
                codesMemory,
                [1, numFrames, NumCodeGroups]
            );

            var isLastArr = new[] { isLast ? 1.0f : 0.0f };
            using var isLastOrt = OrtValue.CreateTensorValueFromMemory(
                OrtMemoryInfo.DefaultInstance,
                isLastArr.AsMemory(),
                [1]
            );

            using var ioBinding = _session.CreateIoBinding();

            ioBinding.BindInput("audio_codes", codesOrt);
            ioBinding.BindInput("is_last", isLastOrt);
            ioBinding.BindInput("pre_conv_history", _preConvHistory);
            ioBinding.BindInput("latent_buffer", _latentBuffer);
            ioBinding.BindInput("conv_history", _convHistory);

            for (var i = 0; i < NumKvLayers; i++)
            {
                ioBinding.BindInput($"past_key_{i}", _pastKeys[i]);
                ioBinding.BindInput($"past_value_{i}", _pastValues[i]);
            }

            // Bind all outputs to CPU
            var cpuMem = OrtMemoryInfo.DefaultInstance;
            ioBinding.BindOutputToDevice("final_wav", cpuMem);
            ioBinding.BindOutputToDevice("valid_samples", cpuMem);
            ioBinding.BindOutputToDevice("next_pre_conv_history", cpuMem);
            ioBinding.BindOutputToDevice("next_latent_buffer", cpuMem);
            ioBinding.BindOutputToDevice("next_conv_history", cpuMem);

            for (var i = 0; i < NumKvLayers; i++)
            {
                ioBinding.BindOutputToDevice($"next_key_{i}", cpuMem);
                ioBinding.BindOutputToDevice($"next_value_{i}", cpuMem);
            }

            _decodeCallCount++;
            _logger?.LogTrace(
                "AudioDecoder: starting ONNX decode #{Call}, {Frames} frames, isLast={IsLast}",
                _decodeCallCount,
                numFrames,
                isLast
            );

            var sw = Stopwatch.StartNew();
            using var runOptions = new RunOptions();
            using var results = _session.RunWithBoundResults(runOptions, ioBinding);
            var onnxMs = sw.ElapsedMilliseconds;

            if (onnxMs > 3000)
            {
                _logger?.LogWarning(
                    "AudioDecoder: ONNX decode #{Call} took {Elapsed}ms for {Frames} frames",
                    _decodeCallCount,
                    onnxMs,
                    numFrames
                );
            }

            // Extract audio — on final chunk, take ALL samples to capture conv buffer flush
            var wavSpan = results[0].GetTensorDataAsSpan<float>();
            int audioLen;
            if (isLast)
            {
                audioLen = wavSpan.Length;
            }
            else
            {
                audioLen = Math.Min((int)results[1].GetTensorDataAsSpan<long>()[0], wavSpan.Length);
            }

            var audio = new float[audioLen];
            for (var i = 0; i < audioLen; i++)
            {
                audio[i] = Math.Clamp(wavSpan[i], -1f, 1f);
            }

            _logger?.LogTrace(
                "AudioDecoder: decode #{Call} produced {Samples} samples in {Elapsed}ms",
                _decodeCallCount,
                audioLen,
                sw.ElapsedMilliseconds
            );

            // Extract, trim, and take ownership of state tensors.
            // Trimming prevents unbounded growth during long utterances.
            // After this block, all state is in our own arrays — results can be disposed.
            var newPreConv = CopyTrimState3D(results[2], PreConvChannels, PreConvWindow);
            var newLatent = CopyTrimState3D(results[3], LatentChannels, LatentWindow);
            var newConv = CopyTrimState3D(results[4], ConvChannels, ConvWindow);

            var newKeys = new OrtValue[NumKvLayers];
            var newValues = new OrtValue[NumKvLayers];
            for (var i = 0; i < NumKvLayers; i++)
            {
                newKeys[i] = CopyTrimState4D(results[5 + i], NumHeads, HeadDim, KvCacheWindow);
                newValues[i] = CopyTrimState4D(
                    results[5 + NumKvLayers + i],
                    NumHeads,
                    HeadDim,
                    KvCacheWindow
                );
            }

            // Swap state
            DisposeState();
            _preConvHistory = newPreConv;
            _latentBuffer = newLatent;
            _convHistory = newConv;
            for (var i = 0; i < NumKvLayers; i++)
            {
                _pastKeys[i] = newKeys[i];
                _pastValues[i] = newValues[i];
            }

            return audio;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisposeState();
        _disposed = true;
    }

    /// <summary>
    ///     Copies and trims a [1, C, T] state tensor to [1, C, min(T, maxT)],
    ///     keeping the last maxT entries along the time dimension.
    ///     Returns a new OrtValue backed by a fresh array (caller owns it).
    /// </summary>
    private static OrtValue CopyTrimState3D(OrtValue source, int channels, int maxT)
    {
        var shape = source.GetTensorTypeAndShape().Shape;
        var t = (int)shape[2];

        if (t <= maxT)
        {
            // No trimming needed — clone the data so we own it
            var clone = source.GetTensorDataAsSpan<float>().ToArray();
            return OrtValue.CreateTensorValueFromMemory(
                OrtMemoryInfo.DefaultInstance,
                clone.AsMemory(),
                shape
            );
        }

        var trimmedT = maxT;
        var skipT = t - trimmedT;
        var result = new float[channels * trimmedT];

        var src = source.GetTensorDataAsSpan<float>();
        for (var c = 0; c < channels; c++)
        {
            src.Slice(c * t + skipT, trimmedT).CopyTo(result.AsSpan(c * trimmedT, trimmedT));
        }

        return OrtValue.CreateTensorValueFromMemory(
            OrtMemoryInfo.DefaultInstance,
            result.AsMemory(),
            [1, channels, trimmedT]
        );
    }

    /// <summary>
    ///     Copies and trims a [1, H, T, D] KV-cache tensor to [1, H, min(T, maxT), D],
    ///     keeping the last maxT entries along the time dimension.
    ///     Returns a new OrtValue backed by a fresh array (caller owns it).
    /// </summary>
    private static OrtValue CopyTrimState4D(OrtValue source, int heads, int headDim, int maxT)
    {
        var shape = source.GetTensorTypeAndShape().Shape;
        var t = (int)shape[2];

        if (t <= maxT)
        {
            var clone = source.GetTensorDataAsSpan<float>().ToArray();
            return OrtValue.CreateTensorValueFromMemory(
                OrtMemoryInfo.DefaultInstance,
                clone.AsMemory(),
                shape
            );
        }

        var trimmedT = maxT;
        var skipT = t - trimmedT;
        var result = new float[heads * trimmedT * headDim];

        var src = source.GetTensorDataAsSpan<float>();
        for (var h = 0; h < heads; h++)
        {
            var srcOffset = h * t * headDim + skipT * headDim;
            var dstOffset = h * trimmedT * headDim;
            src.Slice(srcOffset, trimmedT * headDim)
                .CopyTo(result.AsSpan(dstOffset, trimmedT * headDim));
        }

        return OrtValue.CreateTensorValueFromMemory(
            OrtMemoryInfo.DefaultInstance,
            result.AsMemory(),
            [1, heads, trimmedT, headDim]
        );
    }

    private void InitializeState()
    {
        // All state tensors start with zero-length time dimension
        _preConvHistory = OrtValue.CreateTensorValueFromMemory(
            OrtMemoryInfo.DefaultInstance,
            Memory<float>.Empty,
            [1, PreConvChannels, 0]
        );
        _latentBuffer = OrtValue.CreateTensorValueFromMemory(
            OrtMemoryInfo.DefaultInstance,
            Memory<float>.Empty,
            [1, LatentChannels, 0]
        );
        _convHistory = OrtValue.CreateTensorValueFromMemory(
            OrtMemoryInfo.DefaultInstance,
            Memory<float>.Empty,
            [1, ConvChannels, 0]
        );

        for (var i = 0; i < NumKvLayers; i++)
        {
            _pastKeys[i] = OrtValue.CreateTensorValueFromMemory(
                OrtMemoryInfo.DefaultInstance,
                Memory<float>.Empty,
                [1, NumHeads, 0, HeadDim]
            );
            _pastValues[i] = OrtValue.CreateTensorValueFromMemory(
                OrtMemoryInfo.DefaultInstance,
                Memory<float>.Empty,
                [1, NumHeads, 0, HeadDim]
            );
        }
    }

    private void DisposeState()
    {
        _preConvHistory.Dispose();
        _latentBuffer.Dispose();
        _convHistory.Dispose();

        for (var i = 0; i < NumKvLayers; i++)
        {
            _pastKeys[i].Dispose();
            _pastValues[i].Dispose();
        }
    }
}
