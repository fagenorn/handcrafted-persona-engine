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

    // volatile: _currentOptions is also read lock-free from MinimumSampleCount while the
    // write lock path below publishes a new reference. Reference assignment is atomic on
    // .NET; volatile gives us the release/acquire ordering needed for the writer-reader
    // handoff without taking the ReaderWriterLockSlim on every meter-style read.
    private volatile RVCFilterOptions _currentOptions;

    // Bounded LRU cache of per-voice (predictor, model) pairs used by the audition
    // override path. ONNX session construction is multi-second and GPU-pinned, so we
    // keep the last few auditioned voices alive to make A/B previewing snappy.
    // Access to the cache is serialized by _auditionCacheLock since eviction needs a
    // coherent view of (entries, lru order).
    private readonly Dictionary<string, AuditionCacheEntry> _auditionCache =
        new(StringComparer.Ordinal);

    private readonly LinkedList<string> _auditionCacheLru = new();
    private readonly Lock _auditionCacheLock = new();
    private const int AuditionCacheCapacity = 2;

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

        // Different voice: use the per-voice audition cache so rapid A/B previewing
        // doesn't pay the multi-second ONNX session construction on every call.
        var entry = GetOrLoadAuditionEntry(voice, effectiveOptions.HopSize);

        if (audioSegment.AudioData.Length <= maxSamplesPerChunk)
            ProcessSingleChunk(
                audioSegment,
                effectiveOptions,
                entry.Model,
                entry.Predictor,
                entry.OutputSampleRate
            );
        else
            ProcessInChunks(
                audioSegment,
                effectiveOptions,
                maxSamplesPerChunk,
                entry.Model,
                entry.Predictor,
                entry.OutputSampleRate
            );
    }

    private AuditionCacheEntry GetOrLoadAuditionEntry(string voice, int hopSize)
    {
        lock (_auditionCacheLock)
        {
            if (
                _auditionCache.TryGetValue(voice, out var existing)
                && existing.HopSize == hopSize
            )
            {
                // Promote to most-recently-used.
                _auditionCacheLru.Remove(existing.LruNode);
                _auditionCacheLru.AddFirst(existing.LruNode);
                return existing;
            }

            // Stale entry for this voice (different hop size) — dispose and rebuild.
            if (existing is not null)
            {
                _auditionCacheLru.Remove(existing.LruNode);
                _auditionCache.Remove(voice);
                existing.Predictor.Dispose();
                existing.Model.Dispose();
            }

            var rmvpePath = _modelProvider.GetModelPath(IO.ModelType.Rvc.Rmvpe);
            var hubertPath = _modelProvider.GetModelPath(IO.ModelType.Rvc.Hubert);
            var voiceInfo = _rvcVoiceProvider.GetVoice(voice);

            var predictor = new RmvpeOnnx(rmvpePath);
            var model = new OnnxRVC(voiceInfo.ModelPath, hopSize, hubertPath);

            var node = new LinkedListNode<string>(voice);
            var entry = new AuditionCacheEntry(
                predictor,
                model,
                voiceInfo.OutputSampleRate,
                hopSize,
                node
            );

            _auditionCache[voice] = entry;
            _auditionCacheLru.AddFirst(node);

            // Evict beyond capacity.
            while (_auditionCacheLru.Count > AuditionCacheCapacity)
            {
                var victim = _auditionCacheLru.Last!;
                _auditionCacheLru.RemoveLast();
                if (_auditionCache.Remove(victim.Value, out var evicted))
                {
                    evicted.Predictor.Dispose();
                    evicted.Model.Dispose();
                }
            }

            return entry;
        }
    }

    private sealed record AuditionCacheEntry(
        RmvpeOnnx Predictor,
        OnnxRVC Model,
        int OutputSampleRate,
        int HopSize,
        LinkedListNode<string> LruNode
    );

    public int Priority => 100;

    // _currentOptions is volatile; the reference swap in OnOptionsChanged is released
    // atomically so readers here see a consistent options snapshot without taking the
    // ReaderWriterLockSlim. MinimumSampleCount does not depend on any other field.
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
        var input = audioSegment.AudioData;

        // Upper-bound output size for a single chunk.
        var outputUpperBound = ComputeChunkOutputUpperBound(input.Length, options.HopSize);
        var destination = new float[outputUpperBound];

        var sampleCount = ProcessChunk(
            input,
            originalSampleRate,
            options,
            model,
            predictor,
            voiceOutputRate,
            destination.AsMemory()
        );

        // Slice the backing array to the exact length the filter produced.
        audioSegment.AudioData = destination.AsMemory(0, sampleCount);
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

        // Upper-bound total output length: each chunk's worst case summed in one pass.
        // This is conservative — actual length is accumulated from per-chunk sample counts
        // below — but it lets us allocate the final buffer exactly once.
        var outputUpperBound = 0;
        for (var i = 0; i < inputData.Length; i += maxSamplesPerChunk)
        {
            var remaining = inputData.Length - i;
            var currentChunkSize = Math.Min(maxSamplesPerChunk, remaining);
            outputUpperBound += ComputeChunkOutputUpperBound(currentChunkSize, options.HopSize);
        }

        var destination = new float[outputUpperBound];
        var writePosition = 0;

        for (var i = 0; i < inputData.Length; i += maxSamplesPerChunk)
        {
            var remaining = inputData.Length - i;
            var currentChunkSize = Math.Min(maxSamplesPerChunk, remaining);
            var chunk = inputData.Slice(i, currentChunkSize);

            var written = ProcessChunk(
                chunk,
                originalSampleRate,
                options,
                model,
                predictor,
                voiceOutputRate,
                destination.AsMemory(writePosition)
            );

            writePosition += written;
        }

        // Slice the backing array to the exact total produced.
        audioSegment.AudioData = destination.AsMemory(0, writePosition);
        stopwatch.Stop();
        LogProcessingTime(
            audioSegment.AudioData.Length,
            originalSampleRate,
            stopwatch.Elapsed.TotalSeconds
        );
    }

    /// <summary>
    ///     Writes the processed chunk into <paramref name="destination" /> and returns
    ///     the number of samples written. Caller owns <paramref name="destination" />.
    /// </summary>
    private int ProcessChunk(
        Memory<float> inputChunk,
        uint originalSampleRate,
        RVCFilterOptions options,
        OnnxRVC model,
        IF0Predictor predictor,
        int voiceOutputRate,
        Memory<float> destination
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

        var finalSampleCount = AudioConverter.ResampleFloat(
            processingBuffer.Array.AsMemory(0, processedSampleCount),
            destination,
            1,
            (uint)voiceOutputRate,
            originalSampleRate
        );

        return finalSampleCount;
    }

    /// <summary>
    ///     Upper bound on the final output sample count for a chunk of the given input
    ///     length. The pipeline is: resample input(originalRate) → 16 kHz, reflection-pad
    ///     (trimmed back out by the model), run synthesis at the voice's output rate,
    ///     resample back to originalRate. End-to-end the output length equals the input
    ///     length up to per-stage rounding (each Math.Ceiling can add one sample) and a
    ///     sub-frame residual from the model's pad-trim Math.Round. Adding hopSize plus a
    ///     small constant covers both with margin.
    /// </summary>
    private static int ComputeChunkOutputUpperBound(int inputChunkLength, int hopSize)
    {
        return inputChunkLength + hopSize + 32;
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

        // Drain the audition cache.
        lock (_auditionCacheLock)
        {
            foreach (var entry in _auditionCache.Values)
            {
                entry.Predictor.Dispose();
                entry.Model.Dispose();
            }

            _auditionCache.Clear();
            _auditionCacheLru.Clear();
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
                bool disposedDuringLoad;

                _modelLock.EnterWriteLock();
                try
                {
                    // Dispose may have run while we were loading ONNX sessions above.
                    // If so, don't publish the freshly built instances — dispose them
                    // and leave the field nulls set by DisposeResourcesInternal alone.
                    disposedDuringLoad = _disposed;
                    if (disposedDuringLoad)
                    {
                        oldModel = null;
                        oldPredictor = null;
                    }
                    else
                    {
                        oldModel = _rvcModel;
                        oldPredictor = _f0Predictor;
                        _rvcModel = newModel;
                        _f0Predictor = newPredictor;
                        _voiceOutputSampleRate = newRate;
                        _currentOptions = newOptions;
                    }
                }
                finally
                {
                    _modelLock.ExitWriteLock();
                }

                if (disposedDuringLoad)
                {
                    newPredictor.Dispose();
                    newModel.Dispose();
                    return;
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
