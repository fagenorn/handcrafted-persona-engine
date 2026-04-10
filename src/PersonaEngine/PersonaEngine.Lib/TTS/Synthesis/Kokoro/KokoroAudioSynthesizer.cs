using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using PersonaEngine.Lib.Audio;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.IO;
using PersonaEngine.Lib.Utils.Onnx;
using PersonaEngine.Lib.Utils.Pooling;

namespace PersonaEngine.Lib.TTS.Synthesis.Kokoro;

internal class KokoroAudioSynthesizer : IAsyncDisposable
{
    private const string InputIdsName = "input_ids";

    private const string StyleEmbeddingName = "style";

    private const string SpeedName = "speed";

    private const string WaveformOutputName = "waveform";

    private const string DurationsOutputName = "duration";

    private const float SpeedEpsilon = 0.001f;

    private static readonly long[] SpeedShape = [1];

    private readonly ILogger<KokoroAudioSynthesizer> _logger;

    private readonly IModelProvider _modelProvider;

    private readonly IOptionsMonitor<KokoroVoiceOptions> _optionsMonitor;

    private readonly object _sessionLock = new();

    private readonly IKokoroVoiceProvider _voiceProvider;

    private float? _currentSpeed;

    private bool _disposed;

    private readonly Dictionary<char, long> _phonemeMap;

    private InferenceSession? _session;

    private float[]? _speedData;

    private OrtValue? _speedOrtValue;

    public KokoroAudioSynthesizer(
        IModelProvider ittsModelProvider,
        IKokoroVoiceProvider voiceProvider,
        IOptionsMonitor<KokoroVoiceOptions> options,
        ILogger<KokoroAudioSynthesizer> logger
    )
    {
        _modelProvider = ittsModelProvider;
        _voiceProvider = voiceProvider;
        _optionsMonitor = options;
        _logger = logger;

        _phonemeMap = LoadPhonemeMap();
        InitializeSession();
    }

    public async Task<KokoroSynthesisResult> SynthesizeAsync(
        string phonemes,
        KokoroVoiceOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(phonemes))
        {
            _logger.LogInformation("SynthesizeAsync called with empty or null phonemes.");

            return new KokoroSynthesisResult(Memory<float>.Empty, Memory<long>.Empty);
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var currentOptions = options ?? _optionsMonitor.CurrentValue;

        if (phonemes.Length > currentOptions.MaxPhonemeLength)
        {
            _logger.LogWarning(
                "Input phonemes ({Length}) exceed max length ({MaxLength}). Truncating.",
                phonemes.Length,
                currentOptions.MaxPhonemeLength
            );

            phonemes = phonemes[..currentOptions.MaxPhonemeLength];
        }

        try
        {
            var voice = await _voiceProvider.GetVoiceAsync(
                currentOptions.DefaultVoice,
                cancellationToken
            );

            var session =
                _session ?? throw new ObjectDisposedException(nameof(KokoroAudioSynthesizer));
            using var ioBinding = session.CreateIoBinding();

            var sequenceLength = phonemes.Length;
            using var tokenBuffer = PooledArray<long>.Rent(sequenceLength + 2);

            var inputSize = KokoroTokenConverter.Convert(phonemes, _phonemeMap, tokenBuffer.Array);
            var tokenData = tokenBuffer.Array.AsMemory(0, inputSize);
            var tokenShape = new long[] { 1, tokenData.Length };

            using var inputIdsOrtValue = OrtValue.CreateTensorValueFromMemory(
                OrtMemoryInfo.DefaultInstance,
                tokenData,
                tokenShape
            );
            ioBinding.BindInput(InputIdsName, inputIdsOrtValue);

            var styleEmbeddingData = voice.GetEmbedding([1, inputSize]);
            var styleEmbeddingShape = new long[] { 1, styleEmbeddingData.Length };

            using var styleOrtValue = OrtValue.CreateTensorValueFromMemory(
                OrtMemoryInfo.DefaultInstance,
                styleEmbeddingData,
                styleEmbeddingShape
            );
            ioBinding.BindInput(StyleEmbeddingName, styleOrtValue);

            UpdateAndBindCachedInputs(ioBinding, currentOptions.DefaultSpeed);

            ioBinding.BindOutputToDevice(WaveformOutputName, OrtMemoryInfo.DefaultInstance);
            ioBinding.BindOutputToDevice(DurationsOutputName, OrtMemoryInfo.DefaultInstance);

            Memory<float> waveform;
            Memory<long> durations;

            lock (_sessionLock)
            {
                using var runOptions = new RunOptions();
                using var results = session.RunWithBoundResults(runOptions, ioBinding);

                waveform = results[0].GetTensorDataAsSpan<float>().ToArray().AsMemory();
                durations = results[1].GetTensorDataAsSpan<long>().ToArray().AsMemory();
            }

            if (currentOptions.TrimSilence)
            {
                waveform = AudioSilenceTrimmer.Trim(waveform);
            }

            return new KokoroSynthesisResult(waveform, durations);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error during audio synthesis for phonemes: '{Phonemes}'",
                phonemes
            );

            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        lock (_sessionLock)
        {
            _session?.Dispose();
            _speedOrtValue?.Dispose();

            _session = null;
            _speedOrtValue = null;
            _speedData = null;
        }

        return ValueTask.CompletedTask;
    }

    private void UpdateAndBindCachedInputs(OrtIoBinding ioBinding, float speed)
    {
        if (_currentSpeed is null || MathF.Abs(speed - _currentSpeed.Value) > SpeedEpsilon)
        {
            _speedOrtValue?.Dispose();

            _speedData = [speed];
            _speedOrtValue = OrtValue.CreateTensorValueFromMemory(
                OrtMemoryInfo.DefaultInstance,
                _speedData.AsMemory(),
                SpeedShape
            );
            _currentSpeed = speed;
        }

        ioBinding.BindInput(SpeedName, _speedOrtValue!);
    }

    private Dictionary<char, long> LoadPhonemeMap()
    {
        var mapPath = _modelProvider.GetModelPath(IO.ModelType.Kokoro.PhonemeMappings);

        var lines = File.ReadAllLines(mapPath);
        var mapping = new Dictionary<char, long>(lines.Length);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 3)
            {
                _logger.LogWarning("Skipping invalid line in phoneme map: '{Line}'", line);

                continue;
            }

            mapping[line[0]] = long.Parse(line[2..]);
        }

        _logger.LogInformation(
            "Loaded {Count} phoneme mappings from {Path}.",
            mapping.Count,
            mapPath
        );

        return mapping;
    }

    private void InitializeSession()
    {
        try
        {
            var modelPath = _modelProvider.GetModelPath(IO.ModelType.Kokoro.Synthesis);

            _session = OnnxSessionFactory.Create(modelPath, ExecutionProvider.Cuda);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ONNX Inference Session.");

            throw;
        }
    }
}
