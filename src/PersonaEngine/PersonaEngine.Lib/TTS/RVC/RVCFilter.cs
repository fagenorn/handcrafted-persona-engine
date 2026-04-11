using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Audio;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.IO;
using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.TTS.Synthesis.Audio;
using PersonaEngine.Lib.Utils.Pooling;

namespace PersonaEngine.Lib.TTS.RVC;

// ReSharper disable once InconsistentNaming
public class RVCFilter : IBufferedAudioFilter, IDisposable
{
    private const int ProcessingSampleRate = 16000;

    private const int MaxInputDuration = 30; // seconds

    /// <summary>
    ///     Minimum audio window for reliable voice conversion.
    ///     1 second at 24 kHz. Reflection padding inside OnnxRVC provides the
    ///     additional context HuBERT and F0 predictors need at boundaries.
    /// </summary>
    private const int MinWindowSamples = 24000;

    private readonly SemaphoreSlim _initLock = new(1, 1);

    private readonly ILogger<RVCFilter> _logger;

    private readonly IModelProvider _modelProvider;

    private readonly IDisposable? _optionsChangeRegistration;

    private readonly IOptionsMonitor<RVCFilterOptions> _optionsMonitor;

    private readonly IRVCVoiceProvider _rvcVoiceProvider;

    private RVCFilterOptions _currentOptions;

    private bool _disposed;

    private IF0Predictor? _f0Predictor;

    private OnnxRVC? _rvcModel;

    private int _voiceOutputSampleRate;

    public RVCFilter(
        IOptionsMonitor<RVCFilterOptions> optionsMonitor,
        IModelProvider modelProvider,
        IRVCVoiceProvider rvcVoiceProvider,
        ILogger<RVCFilter> logger
    )
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _modelProvider = modelProvider;
        _rvcVoiceProvider = rvcVoiceProvider;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _currentOptions = optionsMonitor.CurrentValue;

        _ = InitializeAsync(_currentOptions);

        // Register for options changes
        _optionsChangeRegistration = _optionsMonitor.OnChange(OnOptionsChanged);
    }

    public void Process(AudioSegment audioSegment)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RVCFilter));

        if (
            _rvcModel == null
            || _f0Predictor == null
            || audioSegment?.AudioData == null
            || audioSegment.AudioData.Length == 0
        )
            return;

        // Get the latest options for processing
        var options = _currentOptions;
        var originalSampleRate = audioSegment.SampleRate;

        if (!options.Enabled)
            return;

        // Calculate the maximum samples per chunk
        var maxSamplesPerChunk = (int)(originalSampleRate * MaxInputDuration * 0.8);

        // Check if we need to chunk the audio
        if (audioSegment.AudioData.Length <= maxSamplesPerChunk)
            // Process as single chunk
            ProcessSingleChunk(audioSegment, options);
        else
            // Process in chunks
            ProcessInChunks(audioSegment, options, maxSamplesPerChunk);
    }

    public int Priority => 100;

    public int MinimumSampleCount => _currentOptions.Enabled ? MinWindowSamples : 0;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void ProcessSingleChunk(AudioSegment audioSegment, RVCFilterOptions options)
    {
        // Start timing
        var stopwatch = Stopwatch.StartNew();

        var originalSampleRate = (uint)audioSegment.SampleRate;
        var processedBuffer = ProcessChunk(audioSegment.AudioData, originalSampleRate, options);

        audioSegment.AudioData = processedBuffer;

        // Stop timing after processing is complete
        stopwatch.Stop();
        LogProcessingTime(
            audioSegment.AudioData.Length,
            originalSampleRate,
            stopwatch.Elapsed.TotalSeconds
        );
    }

    private void ProcessInChunks(
        AudioSegment audioSegment,
        RVCFilterOptions options,
        int maxSamplesPerChunk
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var originalSampleRate = (uint)audioSegment.SampleRate;
        var inputData = audioSegment.AudioData;

        var chunks = new List<Memory<float>>();

        _logger.LogDebug(
            "Processing audio in chunks. Total samples: {TotalSamples}, Chunk size: {ChunkSize}",
            inputData.Length,
            maxSamplesPerChunk
        );

        // Process chunks sequentially
        for (var i = 0; i < inputData.Length; i += maxSamplesPerChunk)
        {
            var remainingSamples = inputData.Length - i;
            var currentChunkSize = Math.Min(maxSamplesPerChunk, remainingSamples);

            // Extract chunk
            var chunk = inputData.Slice(i, currentChunkSize);

            // Process chunk
            var processedChunk = ProcessChunk(chunk, originalSampleRate, options);

            chunks.Add(processedChunk);

            _logger.LogDebug(
                "Processed chunk {ChunkIndex}/{TotalChunks}",
                chunks.Count,
                (inputData.Length + maxSamplesPerChunk - 1) / maxSamplesPerChunk
            );
        }

        // Combine all chunks
        var combinedBuffer = CombineChunks(chunks);

        audioSegment.AudioData = combinedBuffer;

        stopwatch.Stop();
        LogProcessingTime(
            audioSegment.AudioData.Length,
            originalSampleRate,
            stopwatch.Elapsed.TotalSeconds
        );
    }

    private Memory<float> ProcessChunk(
        Memory<float> inputChunk,
        uint originalSampleRate,
        RVCFilterOptions options
    )
    {
        // Step 1: Resample input to processing sample rate
        // Use proper floating-point ratio calculation
        var resampleRatioToProcessing = (double)ProcessingSampleRate / originalSampleRate;
        var resampledInputSize = (int)Math.Ceiling(inputChunk.Length * resampleRatioToProcessing);

        using var resampledInput = PooledArray<float>.Rent(resampledInputSize);
        var inputSampleCount = AudioConverter.ResampleFloat(
            inputChunk,
            resampledInput.Array,
            1,
            originalSampleRate,
            ProcessingSampleRate
        );

        // Step 2: Process with RVC model
        var voiceOutputRate = _voiceOutputSampleRate;
        var maxInputSamples = voiceOutputRate * MaxInputDuration;
        var outputBufferSize = maxInputSamples + 2 * options.HopSize;

        using var processingBuffer = PooledArray<float>.Rent(outputBufferSize);
        var processedSampleCount = _rvcModel!.ProcessAudio(
            resampledInput.Array.AsMemory(0, inputSampleCount),
            processingBuffer.Array,
            _f0Predictor!,
            options.SpeakerId,
            options.F0UpKey
        );

        // Step 3: Resample from voice output rate back to original sample rate
        var resampleRatioToFinal = (double)originalSampleRate / voiceOutputRate;
        var finalOutputSize = (int)Math.Ceiling(processedSampleCount * resampleRatioToFinal);

        using var resampledOutput = PooledArray<float>.Rent(finalOutputSize);
        var finalSampleCount = AudioConverter.ResampleFloat(
            processingBuffer.Array.AsMemory(0, processedSampleCount),
            resampledOutput.Array,
            1,
            (uint)voiceOutputRate,
            originalSampleRate
        );

        // Create final buffer for this chunk
        var finalBuffer = new float[finalSampleCount];
        Array.Copy(resampledOutput.Array, finalBuffer, finalSampleCount);

        return finalBuffer.AsMemory();
    }

    private Memory<float> CombineChunks(List<Memory<float>> chunks)
    {
        if (chunks.Count == 0)
            return Memory<float>.Empty;

        if (chunks.Count == 1)
            return chunks[0];

        // Calculate total size
        var totalSize = chunks.Sum(c => c.Length);

        var combined = new float[totalSize];
        var currentPosition = 0;

        // Copy all chunks sequentially
        foreach (var chunk in chunks)
        {
            chunk.CopyTo(combined.AsMemory(currentPosition, chunk.Length));
            currentPosition += chunk.Length;
        }

        return combined.AsMemory();
    }

    private void LogProcessingTime(
        int finalSampleCount,
        uint sampleRate,
        double processingTimeSeconds
    )
    {
        var finalAudioDuration = finalSampleCount / (double)sampleRate;
        var realTimeFactor = finalAudioDuration / processingTimeSeconds;

        _logger.LogInformation(
            "Generated {AudioDuration:F2}s audio in {ProcessingTime:F2}s (x{RealTimeFactor:F2} real-time)",
            finalAudioDuration,
            processingTimeSeconds,
            realTimeFactor
        );
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _optionsChangeRegistration?.Dispose();
            DisposeResources();
        }

        _disposed = true;
    }

    private async void OnOptionsChanged(RVCFilterOptions newOptions)
    {
        if (_disposed)
            return;

        if (ShouldReinitialize(newOptions))
        {
            DisposeResources();
            await InitializeAsync(newOptions);
        }

        _currentOptions = newOptions;
    }

    private bool ShouldReinitialize(RVCFilterOptions newOptions)
    {
        return _currentOptions.DefaultVoice != newOptions.DefaultVoice
            || _currentOptions.HopSize != newOptions.HopSize;
    }

    private async ValueTask InitializeAsync(RVCFilterOptions options)
    {
        await _initLock.WaitAsync();
        try
        {
            var rmvpePath = _modelProvider.GetModelPath(IO.ModelType.Rvc.Rmvpe);
            var hubertPath = _modelProvider.GetModelPath(IO.ModelType.Rvc.Hubert);
            var voiceInfo = _rvcVoiceProvider.GetVoice(options.DefaultVoice);

            _f0Predictor = new RmvpeOnnx(rmvpePath);
            // _f0Predictor = new CrepeOnnxSimd(_modelProvider.GetModelPath(IO.ModelType.Rvc.CrepeTiny));
            // _f0Predictor = new ACFMethod(512, 16000);

            _rvcModel = new OnnxRVC(voiceInfo.ModelPath, options.HopSize, hubertPath);
            _voiceOutputSampleRate = voiceInfo.OutputSampleRate;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private void DisposeResources()
    {
        _rvcModel?.Dispose();
        _rvcModel = null;

        _f0Predictor?.Dispose();
        _f0Predictor = null;
    }
}
