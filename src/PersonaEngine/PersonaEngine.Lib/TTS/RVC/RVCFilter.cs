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

    // Protects _rvcModel, _f0Predictor, _voiceOutputSampleRate from torn reads across
    // Process() and OnOptionsChanged() / InitializeAsync() / DisposeResources().
    private readonly ReaderWriterLockSlim _modelLock = new(LockRecursionPolicy.NoRecursion);

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

        if (audioSegment?.AudioData == null || audioSegment.AudioData.Length == 0)
            return;

        OnnxRVC? model;
        IF0Predictor? predictor;
        int voiceOutputRate;
        RVCFilterOptions options;

        _modelLock.EnterReadLock();
        try
        {
            model = _rvcModel;
            predictor = _f0Predictor;
            voiceOutputRate = _voiceOutputSampleRate;
            options = _currentOptions;
        }
        finally
        {
            _modelLock.ExitReadLock();
        }

        if (model is null || predictor is null || !options.Enabled)
            return;

        var originalSampleRate = audioSegment.SampleRate;
        var maxSamplesPerChunk = (int)(originalSampleRate * MaxInputDuration * 0.8);

        if (audioSegment.AudioData.Length <= maxSamplesPerChunk)
            ProcessSingleChunk(audioSegment, options, model, predictor, voiceOutputRate);
        else
            ProcessInChunks(
                audioSegment,
                options,
                maxSamplesPerChunk,
                model,
                predictor,
                voiceOutputRate
            );
    }

    /// <summary>
    ///     One-off processing with per-call overrides for voice and pitch.
    ///     Used by the voice-audition pipeline to preview an RVC voice without mutating
    ///     the ambient options consumed by the live conversation session.
    /// </summary>
    public void Process(AudioSegment audioSegment, RvcOverride? @override)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RVCFilter));

        if (audioSegment?.AudioData == null || audioSegment.AudioData.Length == 0)
            return;

        if (@override is null)
        {
            Process(audioSegment);
            return;
        }

        // Snapshot ambient options under the read lock so we know what voice is loaded.
        RVCFilterOptions baseOptions;
        _modelLock.EnterReadLock();
        try
        {
            baseOptions = _currentOptions;
        }
        finally
        {
            _modelLock.ExitReadLock();
        }

        var voice = @override.Voice ?? baseOptions.DefaultVoice;
        var f0UpKey = @override.F0UpKey ?? baseOptions.F0UpKey;

        var effectiveOptions = baseOptions with
        {
            DefaultVoice = voice,
            F0UpKey = f0UpKey,
            Enabled = true,
        };

        var originalSampleRate = audioSegment.SampleRate;
        var maxSamplesPerChunk = (int)(originalSampleRate * MaxInputDuration * 0.8);

        if (string.Equals(voice, baseOptions.DefaultVoice, StringComparison.Ordinal))
        {
            // Same voice: reuse the ambient model under the read lock.
            OnnxRVC? model;
            IF0Predictor? predictor;
            int voiceOutputRate;

            _modelLock.EnterReadLock();
            try
            {
                model = _rvcModel;
                predictor = _f0Predictor;
                voiceOutputRate = _voiceOutputSampleRate;
            }
            finally
            {
                _modelLock.ExitReadLock();
            }

            if (model is null || predictor is null)
                return;

            if (audioSegment.AudioData.Length <= maxSamplesPerChunk)
                ProcessSingleChunk(
                    audioSegment,
                    effectiveOptions,
                    model,
                    predictor,
                    voiceOutputRate
                );
            else
                ProcessInChunks(
                    audioSegment,
                    effectiveOptions,
                    maxSamplesPerChunk,
                    model,
                    predictor,
                    voiceOutputRate
                );
            return;
        }

        // Different voice: load a throwaway instance for this call only.
        var rmvpePath = _modelProvider.GetModelPath(IO.ModelType.Rvc.Rmvpe);
        var hubertPath = _modelProvider.GetModelPath(IO.ModelType.Rvc.Hubert);
        var voiceInfo = _rvcVoiceProvider.GetVoice(voice);

        using var throwawayPredictor = new RmvpeOnnx(rmvpePath);
        using var throwawayModel = new OnnxRVC(
            voiceInfo.ModelPath,
            effectiveOptions.HopSize,
            hubertPath
        );

        if (audioSegment.AudioData.Length <= maxSamplesPerChunk)
            ProcessSingleChunk(
                audioSegment,
                effectiveOptions,
                throwawayModel,
                throwawayPredictor,
                voiceInfo.OutputSampleRate
            );
        else
            ProcessInChunks(
                audioSegment,
                effectiveOptions,
                maxSamplesPerChunk,
                throwawayModel,
                throwawayPredictor,
                voiceInfo.OutputSampleRate
            );
    }

    public int Priority => 100;

    // _currentOptions holds an immutable record; reference assignment is atomic on .NET,
    // so this read does not require the lock.
    public int MinimumSampleCount => _currentOptions.Enabled ? MinWindowSamples : 0;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void ProcessSingleChunk(
        AudioSegment audioSegment,
        RVCFilterOptions options,
        OnnxRVC model,
        IF0Predictor predictor,
        int voiceOutputRate
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var originalSampleRate = (uint)audioSegment.SampleRate;
        var processedBuffer = ProcessChunk(
            audioSegment.AudioData,
            originalSampleRate,
            options,
            model,
            predictor,
            voiceOutputRate
        );

        audioSegment.AudioData = processedBuffer;
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
        int maxSamplesPerChunk,
        OnnxRVC model,
        IF0Predictor predictor,
        int voiceOutputRate
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var originalSampleRate = (uint)audioSegment.SampleRate;
        var inputData = audioSegment.AudioData;

        var chunks = new List<Memory<float>>();

        for (var i = 0; i < inputData.Length; i += maxSamplesPerChunk)
        {
            var remainingSamples = inputData.Length - i;
            var currentChunkSize = Math.Min(maxSamplesPerChunk, remainingSamples);
            var chunk = inputData.Slice(i, currentChunkSize);
            var processedChunk = ProcessChunk(
                chunk,
                originalSampleRate,
                options,
                model,
                predictor,
                voiceOutputRate
            );
            chunks.Add(processedChunk);
        }

        audioSegment.AudioData = CombineChunks(chunks);
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
        RVCFilterOptions options,
        OnnxRVC model,
        IF0Predictor predictor,
        int voiceOutputRate
    )
    {
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

        var maxInputSamples = voiceOutputRate * MaxInputDuration;
        var outputBufferSize = maxInputSamples + 2 * options.HopSize;

        using var processingBuffer = PooledArray<float>.Rent(outputBufferSize);
        var processedSampleCount = model.ProcessAudio(
            resampledInput.Array.AsMemory(0, inputSampleCount),
            processingBuffer.Array,
            predictor,
            options.SpeakerId,
            options.F0UpKey
        );

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

        var totalSize = chunks.Sum(c => c.Length);

        var combined = new float[totalSize];
        var currentPosition = 0;

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
            DisposeResourcesInternal();
            _modelLock.Dispose();
            _initLock.Dispose();
        }

        _disposed = true;
    }

    private void DisposeResourcesInternal()
    {
        _modelLock.EnterWriteLock();
        try
        {
            _rvcModel?.Dispose();
            _rvcModel = null;

            _f0Predictor?.Dispose();
            _f0Predictor = null;
        }
        finally
        {
            _modelLock.ExitWriteLock();
        }
    }

    private void OnOptionsChanged(RVCFilterOptions newOptions)
    {
        if (_disposed)
            return;

        try
        {
            if (ShouldReinitialize(newOptions))
            {
                // Build new model OUTSIDE the write lock (ONNX load can take seconds).
                var rmvpePath = _modelProvider.GetModelPath(IO.ModelType.Rvc.Rmvpe);
                var hubertPath = _modelProvider.GetModelPath(IO.ModelType.Rvc.Hubert);
                var voiceInfo = _rvcVoiceProvider.GetVoice(newOptions.DefaultVoice);

                var newPredictor = new RmvpeOnnx(rmvpePath);
                var newModel = new OnnxRVC(voiceInfo.ModelPath, newOptions.HopSize, hubertPath);
                var newRate = voiceInfo.OutputSampleRate;

                OnnxRVC? oldModel;
                IF0Predictor? oldPredictor;

                _modelLock.EnterWriteLock();
                try
                {
                    oldModel = _rvcModel;
                    oldPredictor = _f0Predictor;
                    _rvcModel = newModel;
                    _f0Predictor = newPredictor;
                    _voiceOutputSampleRate = newRate;
                    _currentOptions = newOptions;
                }
                finally
                {
                    _modelLock.ExitWriteLock();
                }

                // Safe to dispose old instances: no reader can still be holding them.
                oldModel?.Dispose();
                oldPredictor?.Dispose();
            }
            else
            {
                // No model swap needed — just update options atomically.
                _modelLock.EnterWriteLock();
                try
                {
                    _currentOptions = newOptions;
                }
                finally
                {
                    _modelLock.ExitWriteLock();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to reinitialize RVC model after options change. Options will not be updated."
            );
        }
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

            var newPredictor = new RmvpeOnnx(rmvpePath);
            var newModel = new OnnxRVC(voiceInfo.ModelPath, options.HopSize, hubertPath);

            _modelLock.EnterWriteLock();
            try
            {
                _f0Predictor = newPredictor;
                _rvcModel = newModel;
                _voiceOutputSampleRate = voiceInfo.OutputSampleRate;
            }
            finally
            {
                _modelLock.ExitWriteLock();
            }
        }
        finally
        {
            _initLock.Release();
        }
    }
}
