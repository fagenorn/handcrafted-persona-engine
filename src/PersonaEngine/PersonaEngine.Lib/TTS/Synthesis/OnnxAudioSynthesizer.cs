using System.Buffers;
using System.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Implementation of audio synthesis using ONNX models
/// </summary>
public class OnnxAudioSynthesizer : IAudioSynthesizer
{
    private readonly ITtsCache _cache;

    private readonly SemaphoreSlim _inferenceThrottle;

    private readonly IModelProvider _ittsModelProvider;

    private readonly ILogger<OnnxAudioSynthesizer> _logger;

    private readonly IOptionsMonitor<KokoroVoiceOptions> _options;

    private readonly IKokoroVoiceProvider _voiceProvider;

    private bool _disposed;

    private IReadOnlyDictionary<char, long>? _phonemeToIdMap;

    private InferenceSession? _synthesisSession;

    public OnnxAudioSynthesizer(
        IModelProvider                      ittsModelProvider,
        IKokoroVoiceProvider                voiceProvider,
        ITtsCache                           cache,
        IOptionsMonitor<KokoroVoiceOptions> options,
        ILogger<OnnxAudioSynthesizer>       logger)
    {
        _ittsModelProvider = ittsModelProvider ?? throw new ArgumentNullException(nameof(ittsModelProvider));
        _voiceProvider     = voiceProvider ?? throw new ArgumentNullException(nameof(voiceProvider));
        _cache             = cache ?? throw new ArgumentNullException(nameof(cache));
        _options           = options ?? throw new ArgumentNullException(nameof(options));
        _logger            = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create throttle to limit concurrent inference operations
        _inferenceThrottle = new SemaphoreSlim(
                                               Math.Max(1, Environment.ProcessorCount / 2), // Use half of available cores for inference
                                               Environment.ProcessorCount);

        _logger.LogInformation("Initialized ONNX audio synthesizer");
    }

    /// <summary>
    ///     Synthesizes audio from phonemes
    /// </summary>
    public async Task<AudioData> SynthesizeAsync(
        string              phonemes,
        KokoroVoiceOptions? options           = null,
        CancellationToken   cancellationToken = default)
    {
        if ( string.IsNullOrEmpty(phonemes) )
        {
            return new AudioData(Array.Empty<float>(), Array.Empty<long>());
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Initialize lazily on first use
            await EnsureInitializedAsync(cancellationToken);

            // Get current options (to ensure we have the latest values)
            var currentOptions = options ?? _options.CurrentValue;

            // Validate phoneme length
            if ( phonemes.Length > currentOptions.MaxPhonemeLength )
            {
                _logger.LogWarning("Truncating phonemes to maximum length {MaxLength}", currentOptions.MaxPhonemeLength);
                phonemes = phonemes.Substring(0, currentOptions.MaxPhonemeLength);
            }

            // Convert phonemes to tokens
            var tokens = ConvertPhonemesToTokens(phonemes);
            if ( tokens.Count == 0 )
            {
                _logger.LogWarning("No valid tokens generated from phonemes");

                return new AudioData(Array.Empty<float>(), Array.Empty<long>());
            }

            // Get voice data
            var voice = await _voiceProvider.GetVoiceAsync(currentOptions.DefaultVoice, cancellationToken);

            // Create audio with throttling for inference
            await _inferenceThrottle.WaitAsync(cancellationToken);

            try
            {
                // Measure performance
                var timer = Stopwatch.StartNew();

                // Perform inference
                var (audioData, phonemeTimings) = await RunInferenceAsync(tokens, voice, options, cancellationToken);

                // Log performance metrics
                timer.Stop();
                LogPerformanceMetrics(timer.Elapsed, audioData.Length, phonemes.Length, options);

                return new AudioData(audioData, phonemeTimings);
            }
            finally
            {
                _inferenceThrottle.Release();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Audio synthesis was canceled");

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during audio synthesis");

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if ( _disposed )
        {
            return;
        }

        _synthesisSession?.Dispose();
        _synthesisSession = null;

        _phonemeToIdMap = null;
        _inferenceThrottle.Dispose();

        _disposed = true;

        await Task.CompletedTask;
    }

    /// <summary>
    ///     Ensures the model and resources are initialized
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if ( _synthesisSession != null && _phonemeToIdMap != null )
        {
            return;
        }

        // Load model and resources
        await InitializeSessionAsync(cancellationToken);
        await LoadPhonemeMapAsync(cancellationToken);
    }

    /// <summary>
    ///     Initializes the ONNX inference session
    /// </summary>
    private async Task InitializeSessionAsync(CancellationToken cancellationToken)
    {
        if ( _synthesisSession != null )
        {
            return;
        }

        _logger.LogInformation("Initializing synthesis model");

        // Create optimized session options
        var sessionOptions = new SessionOptions {
                                                    EnableMemoryPattern    = true,
                                                    ExecutionMode          = ExecutionMode.ORT_PARALLEL,
                                                    InterOpNumThreads      = Math.Max(1, Environment.ProcessorCount / 2),
                                                    IntraOpNumThreads      = Math.Max(1, Environment.ProcessorCount / 2),
                                                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                                                    LogSeverityLevel       = OrtLoggingLevel.ORT_LOGGING_LEVEL_FATAL
                                                };

        try
        {
            sessionOptions.AppendExecutionProvider_CUDA();
            _logger.LogInformation("CUDA execution provider added successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("CUDA execution provider not available: {Message}. Using CPU.", ex.Message);
        }

        // Get model with retry mechanism
        var        maxRetries    = 3;
        Exception? lastException = null;

        for ( var attempt = 0; attempt < maxRetries; attempt++ )
        {
            try
            {
                var model     = await _ittsModelProvider.GetModelAsync(ModelType.KokoroSynthesis, cancellationToken);
                var modelData = await model.GetDataAsync();

                _synthesisSession = new InferenceSession(modelData, sessionOptions);
                _logger.LogInformation("Synthesis model initialized successfully");

                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Error initializing ONNX session (attempt {Attempt} of {MaxRetries}). Retrying...",
                                   attempt + 1, maxRetries);

                if ( attempt < maxRetries - 1 )
                {
                    // Exponential backoff
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
                }
            }
        }

        // If we get here, all attempts failed
        throw new InvalidOperationException(
                                            $"Failed to initialize ONNX session after {maxRetries} attempts", lastException);
    }

    /// <summary>
    ///     Loads the phoneme-to-ID mapping
    /// </summary>
    private async Task LoadPhonemeMapAsync(CancellationToken cancellationToken)
    {
        if ( _phonemeToIdMap != null )
        {
            return;
        }

        _logger.LogInformation("Loading phoneme mapping");

        try
        {
            // Use cache for phoneme map to avoid repeated loading
            _phonemeToIdMap = await _cache.GetOrAddAsync("phoneme_map", async ct =>
                                                                        {
                                                                            var model   = await _ittsModelProvider.GetModelAsync(ModelType.KokoroPhonemeMappings, ct);
                                                                            var mapPath = model.Path;

                                                                            _logger.LogDebug("Loading phoneme mapping from {Path}", mapPath);

                                                                            var lines = await File.ReadAllLinesAsync(mapPath, ct);

                                                                            // Initialize with capacity for better performance
                                                                            var mapping = new Dictionary<char, long>(lines.Length);

                                                                            foreach ( var line in lines )
                                                                            {
                                                                                if ( string.IsNullOrWhiteSpace(line) || line.Length < 3 )
                                                                                {
                                                                                    continue;
                                                                                }

                                                                                mapping[line[0]] = long.Parse(line[2..]);
                                                                            }

                                                                            _logger.LogInformation("Loaded {Count} phoneme mappings", mapping.Count);

                                                                            return mapping;
                                                                        }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            /* Ignored */
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading phoneme mapping");

            throw;
        }
    }

    /// <summary>
    ///     Converts phoneme characters to token IDs
    /// </summary>
    private List<long> ConvertPhonemesToTokens(string phonemes)
    {
        // If no mapping loaded, can't convert
        if ( _phonemeToIdMap == null )
        {
            throw new InvalidOperationException("Phoneme map not initialized");
        }

        // Pre-allocate with expected capacity
        var tokens = new List<long>(phonemes.Length);

        foreach ( var phoneme in phonemes )
        {
            if ( _phonemeToIdMap.TryGetValue(phoneme, out var id) )
            {
                tokens.Add(id);
            }
        }

        return tokens;
    }

    /// <summary>
    ///     Runs model inference to generate audio
    /// </summary>
    private async Task<(Memory<float> AudioData, Memory<long> PhonemeTimings)> RunInferenceAsync(
        List<long>          tokens,
        VoiceData           voice,
        KokoroVoiceOptions? options           = null,
        CancellationToken   cancellationToken = default)
    {
        // Make sure session is initialized
        if ( _synthesisSession == null )
        {
            throw new InvalidOperationException("Synthesis session not initialized");
        }

        // Get current options (to ensure we have the latest values)
        var currentOptions = options ?? _options.CurrentValue;

        // Create model inputs
        var modelInputs = CreateModelInputs(tokens, voice, currentOptions.DefaultSpeed);

        // Run inference
        using var results = _synthesisSession.Run(modelInputs);

        // Extract results
        var waveformTensor = results[0].AsTensor<float>();
        var durationTensor = results[1].AsTensor<long>();

        // Extract result data
        var waveformLength = waveformTensor.Dimensions.Length > 0 ? waveformTensor.Dimensions[0] : 0;

        var waveform  = new float[waveformLength];
        var durations = new long[durationTensor.Length];

        // Copy data to results
        Buffer.BlockCopy(waveformTensor.ToArray(), 0, waveform, 0, waveformLength * sizeof(float));
        Buffer.BlockCopy(durationTensor.ToArray(), 0, durations, 0, durations.Length * sizeof(long));

        // Apply audio processing if needed
        if ( currentOptions.TrimSilence )
        {
            waveform = TrimSilence(waveform);
        }

        return (waveform, durations);
    }

    /// <summary>
    ///     Creates input tensors for the synthesis model
    /// </summary>
    private List<NamedOnnxValue> CreateModelInputs(List<long> tokens, VoiceData voice, float speed)
    {
        // Add boundary tokens (BOS/EOS)
        var tokenArray = ArrayPool<long>.Shared.Rent(tokens.Count + 2);

        try
        {
            // BOS token
            tokenArray[0] = 0;

            // Copy tokens
            tokens.CopyTo(tokenArray, 1);

            // EOS token
            tokenArray[tokens.Count + 1] = 0;

            // Create tensors
            var inputTokens = new DenseTensor<long>(
                                                    tokenArray.AsMemory(0, tokens.Count + 2),
                                                    new[] { 1, tokens.Count + 2 });

            var styleInput     = voice.GetEmbedding(inputTokens.Dimensions).ToArray();
            var voiceEmbedding = new DenseTensor<float>(styleInput, new[] { 1, styleInput.Length });

            var speedTensor = new DenseTensor<float>(
                                                     new[] { speed },
                                                     new[] { 1 });

            // Return named tensors
            return new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input_ids", inputTokens), NamedOnnxValue.CreateFromTensor("style", voiceEmbedding), NamedOnnxValue.CreateFromTensor("speed", speedTensor) };
        }
        finally
        {
            // Always return the rented array
            ArrayPool<long>.Shared.Return(tokenArray);
        }
    }

    /// <summary>
    ///     Trims silence from the beginning and end of audio data
    /// </summary>
    private float[] TrimSilence(float[] audioData, float threshold = 0.01f, int minSamples = 512)
    {
        if ( audioData.Length <= minSamples * 2 )
        {
            return audioData;
        }

        // Find start (first sample above threshold)
        var startIndex = 0;
        for ( var i = 0; i < audioData.Length - minSamples; i++ )
        {
            if ( Math.Abs(audioData[i]) > threshold )
            {
                startIndex = Math.Max(0, i - minSamples);

                break;
            }
        }

        // Find end (last sample above threshold)
        var endIndex = audioData.Length - 1;
        for ( var i = audioData.Length - 1; i >= minSamples; i-- )
        {
            if ( Math.Abs(audioData[i]) > threshold )
            {
                endIndex = Math.Min(audioData.Length - 1, i + minSamples);

                break;
            }
        }

        // If no significant audio found, return original
        if ( startIndex >= endIndex )
        {
            return audioData;
        }

        // Create trimmed array
        var newLength = endIndex - startIndex + 1;
        var result    = new float[newLength];
        Array.Copy(audioData, startIndex, result, 0, newLength);

        return result;
    }

    /// <summary>
    ///     Logs performance metrics for inference
    /// </summary>
    private void LogPerformanceMetrics(TimeSpan elapsed, int audioLength, int phonemeCount, KokoroVoiceOptions? options = null)
    {
        // Get current options to ensure we have the latest sample rate
        var currentOptions = options ?? _options.CurrentValue;

        var audioDuration  = audioLength / (float)currentOptions.SampleRate;
        var elapsedSeconds = elapsed.TotalSeconds;
        var speedup        = elapsedSeconds > 0 ? audioDuration / elapsedSeconds : 0;

        _logger.LogInformation(
                               "Generated {AudioDuration:F2}s audio for {PhonemeCount} phonemes in {Elapsed:F2}s (x{Speedup:F2} real-time)",
                               audioDuration, phonemeCount, elapsedSeconds, speedup);
    }
}