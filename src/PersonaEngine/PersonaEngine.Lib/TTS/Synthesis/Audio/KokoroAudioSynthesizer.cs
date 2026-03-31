using System.Buffers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.IO;

namespace PersonaEngine.Lib.TTS.Synthesis;

public class KokoroAudioSynthesizer : IAudioSynthesizer
{
    private const string InputIdsName = "input_ids";

    private const string StyleEmbeddingName = "style";

    private const string SpeedName = "speed";

    private const string WaveformOutputName = "waveform";

    private const string DurationsOutputName = "duration";

    private const long BosTokenId = 0;

    private const long EosTokenId = 0;

    private readonly ILogger<KokoroAudioSynthesizer> _logger;

    private readonly IModelProvider _modelProvider;

    private readonly IOptionsMonitor<KokoroVoiceOptions> _optionsMonitor;

    private readonly IKokoroVoiceProvider _voiceProvider;

    private float? _currentSpeed;

    private Dictionary<char, long> _phonemeMap = null!;

    private InferenceSession? _session;

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

        LoadPhonemeMap();
        InitializeSession();
    }

    public async Task<AudioData> SynthesizeAsync(
        string phonemes,
        KokoroVoiceOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(phonemes))
        {
            _logger.LogInformation("SynthesizeAsync called with empty or null phonemes.");

            return new AudioData(Memory<float>.Empty, Memory<long>.Empty);
        }

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

            using var ioBinding = _session!.CreateIoBinding();

            var sequenceLength = phonemes.Length;
            var tokenBuffer = ArrayPool<long>.Shared.Rent(sequenceLength + 2);
            OrtValue? inputIdsOrtValue;
            int inputSize;
            try
            {
                inputSize = ConvertPhonemesToTokens(phonemes, tokenBuffer);

                var tokenData = tokenBuffer.AsMemory(0, inputSize);
                var tokenShape = new long[] { 1, tokenData.Length };

                inputIdsOrtValue = OrtValue.CreateTensorValueFromMemory(
                    OrtMemoryInfo.DefaultInstance,
                    tokenData,
                    tokenShape
                );
                ioBinding.BindInput(InputIdsName, inputIdsOrtValue);
            }
            finally
            {
                ArrayPool<long>.Shared.Return(tokenBuffer);
            }

            var styleEmbeddingData = voice.GetEmbedding([1, inputSize]);
            var styleEmbeddingShape = new long[] { 1, styleEmbeddingData.Length };

            var styleOrtValue = OrtValue.CreateTensorValueFromMemory(
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

            lock (_session)
            {
                using var runOptions = new RunOptions();
                using var results = _session.RunWithBoundResults(runOptions, ioBinding);

                var waveformTensor = results[0].GetTensorDataAsSpan<float>();
                var durationTensor = results[1].GetTensorDataAsSpan<long>();

                waveform = waveformTensor.ToArray().AsMemory();
                durations = durationTensor.ToArray().AsMemory();
            }

            if (currentOptions.TrimSilence)
            {
                waveform = TrimSilence(waveform);
            }

            inputIdsOrtValue?.Dispose();

            return new AudioData(waveform, durations);
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
        _session?.Dispose();
        _speedOrtValue?.Dispose();

        _session = null;
        _speedOrtValue = null;

        return ValueTask.CompletedTask;
    }

    private void UpdateAndBindCachedInputs(OrtIoBinding ioBinding, float speed)
    {
        if (
            _currentSpeed == null
            || speed - _currentSpeed > 0.001f
            || speed - _currentSpeed < -0.001f
        )
        {
            _speedOrtValue?.Dispose();

            var speedData = new[] { speed };
            var speedShape = new long[] { 1 };

            _speedOrtValue = OrtValue.CreateTensorValueFromMemory(
                OrtMemoryInfo.DefaultInstance,
                speedData.AsMemory(),
                speedShape
            );
            _currentSpeed = speed;
        }

        if (_speedOrtValue != null)
        {
            ioBinding.BindInput(SpeedName, _speedOrtValue);
        }
    }

    private void LoadPhonemeMap()
    {
        var mapPath = _modelProvider.GetModelPath(IO.ModelType.KokoroPhonemeMappings);

        try
        {
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

            _phonemeMap = mapping;
            _logger.LogInformation(
                "Loaded {Count} phoneme mappings from {Path}.",
                mapping.Count,
                mapPath
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading or parsing phoneme map file: {MapPath}", mapPath);

            throw;
        }
    }

    private void InitializeSession()
    {
        var sessionOptions = new SessionOptions
        {
            EnableMemoryPattern = true,
            ExecutionMode = ExecutionMode.ORT_PARALLEL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };

        sessionOptions.AppendExecutionProvider_CUDA();

        try
        {
            var modelPath = _modelProvider.GetModelPath(IO.ModelType.KokoroSynthesis);

            _session = new InferenceSession(modelPath, sessionOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ONNX Inference Session.");
            sessionOptions.Dispose(); // Dispose options if session creation failed

            throw;
        }
    }

    private int ConvertPhonemesToTokens(string phonemes, long[] buffer)
    {
        if (_phonemeMap == null)
        {
            _logger.LogError("Phoneme map accessed before initialization.");

            throw new InvalidOperationException(
                "Phoneme map is not initialized. Ensure EnsureInitializedAsync() was called and completed successfully."
            );
        }

        buffer[0] = BosTokenId;

        for (var i = 0; i < phonemes.Length; i++)
        {
            var phonemeChar = phonemes[i];
            if (_phonemeMap.TryGetValue(phonemeChar, out var id))
            {
                buffer[i + 1] = id;
            }
            else
            {
                _logger.LogWarning(
                    "Unrecognized phoneme character '{PhonemeChar}' encountered. Skipping.",
                    phonemeChar
                );
            }
        }

        buffer[phonemes.Length + 1] = EosTokenId;

        return phonemes.Length + 1;
    }

    private Memory<float> TrimSilence(
        Memory<float> audioData,
        float threshold = 0.01f,
        int minSamplesPadding = 512
    )
    {
        if (audioData.IsEmpty || audioData.Length <= minSamplesPadding * 2)
        {
            return audioData;
        }

        var span = audioData.Span;
        var startIndex = -1;
        var endIndex = -1;

        for (var i = 0; i < span.Length; i++)
        {
            if (Math.Abs(span[i]) > threshold)
            {
                startIndex = Math.Max(0, i - minSamplesPadding);

                break;
            }
        }

        if (startIndex == -1)
        {
            _logger.LogDebug(
                "Audio appears entirely below silence threshold. Returning original or empty."
            );

            return audioData;
        }

        for (var i = span.Length - 1; i >= 0; i--) // Iterate down to 0
        {
            if (Math.Abs(span[i]) > threshold)
            {
                endIndex = Math.Min(span.Length - 1, i + minSamplesPadding);

                break;
            }
        }

        if (endIndex == -1)
        {
            endIndex = span.Length - 1;
            _logger.LogWarning(
                "Could not find end index for silence trimming after finding start index. Using full length from start."
            );
        }

        if (startIndex >= endIndex)
        {
            _logger.LogDebug(
                "Silence trimming resulted in invalid range (Start: {StartIndex}, End: {EndIndex}). Returning original audio.",
                startIndex,
                endIndex
            );

            return audioData;
        }

        var length = endIndex - startIndex + 1;
        if (length == audioData.Length)
        {
            _logger.LogDebug(
                "No silence trimmed. Original length: {OriginalLength}",
                audioData.Length
            );

            return audioData;
        }

        _logger.LogDebug(
            "Trimming silence: Original length {OriginalLength}, New length {NewLength} (Start: {StartIndex}, End: {EndIndex})",
            audioData.Length,
            length,
            startIndex,
            endIndex
        );

        return audioData.Slice(startIndex, length);
    }
}
