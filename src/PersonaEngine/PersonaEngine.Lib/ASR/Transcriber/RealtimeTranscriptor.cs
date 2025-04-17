using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.ASR.VAD;
using PersonaEngine.Lib.Audio;

namespace PersonaEngine.Lib.ASR.Transcriber;

internal class RealtimeTranscriptor : IRealtimeSpeechTranscriptor, IAsyncDisposable
{
    private readonly object cacheLock = new();

    private readonly ILogger<RealtimeTranscriptor> logger;

    private readonly RealtimeSpeechTranscriptorOptions options;

    private readonly RealtimeOptions realtimeOptions;

    private readonly ISpeechTranscriptorFactory? recognizingSpeechTranscriptorFactory;

    private readonly ISpeechTranscriptorFactory speechTranscriptorFactory;

    private readonly Dictionary<string, ISpeechTranscriptor> transcriptorCache;

    private readonly IVadDetector vadDetector;

    private TimeSpan _lastDuration = TimeSpan.Zero;

    private TimeSpan _processedDuration = TimeSpan.Zero;

    private bool isDisposed;

    public RealtimeTranscriptor(
        ISpeechTranscriptorFactory        speechTranscriptorFactory,
        IVadDetector                      vadDetector,
        ISpeechTranscriptorFactory?       recognizingSpeechTranscriptorFactory,
        RealtimeSpeechTranscriptorOptions options,
        RealtimeOptions                   realtimeOptions,
        ILogger<RealtimeTranscriptor>     logger)
    {
        this.speechTranscriptorFactory            = speechTranscriptorFactory;
        this.vadDetector                          = vadDetector;
        this.recognizingSpeechTranscriptorFactory = recognizingSpeechTranscriptorFactory;
        this.options                              = options;
        this.realtimeOptions                      = realtimeOptions;
        this.logger                               = logger;
        transcriptorCache                         = new Dictionary<string, ISpeechTranscriptor>();

        logger.LogDebug("RealtimeTranscriptor initialized with options: {@Options}, realtime options: {@RealtimeOptions}",
                        options, realtimeOptions);
    }

    public async ValueTask DisposeAsync()
    {
        if ( isDisposed )
        {
            return;
        }

        // lock (cacheLock)
        // {
        logger.LogInformation("Disposing {Count} cached transcriptors", transcriptorCache.Count);
        foreach ( var transcriptor in transcriptorCache.Values )
        {
            await transcriptor.DisposeAsync();
        }

        transcriptorCache.Clear();
        // }

        isDisposed = true;
        GC.SuppressFinalize(this);
    }

    public async IAsyncEnumerable<IRealtimeRecognitionEvent> TranscribeAsync(
        IAwaitableAudioSource                      source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if ( isDisposed )
        {
            throw new ObjectDisposedException(nameof(RealtimeTranscriptor));
        }

        var          stopwatch        = Stopwatch.StartNew();
        var          promptBuilder    = new StringBuilder(options.Prompt);
        CultureInfo? detectedLanguage = null;

        await source.WaitForInitializationAsync(cancellationToken);
        var sessionId = Guid.NewGuid().ToString();

        logger.LogInformation("Starting transcription session {SessionId}", sessionId);

        yield return new RealtimeSessionStarted(sessionId);

        try
        {
            while ( !source.IsFlushed )
            {
                var currentDuration = source.Duration;

                if ( currentDuration == _lastDuration )
                {
                    await source.WaitForNewSamplesAsync(_lastDuration + realtimeOptions.ProcessingInterval, cancellationToken);

                    continue;
                }

                logger.LogTrace("Processing new audio segment: Current={Current}ms, Last={Last}ms, Delta={Delta}ms",
                                currentDuration.TotalMilliseconds,
                                _lastDuration.TotalMilliseconds,
                                (currentDuration - _lastDuration).TotalMilliseconds);

                _lastDuration = currentDuration;
                var segmentStopwatch = Stopwatch.StartNew();
                var slicedSource     = new SliceAudioSource(source, _processedDuration, currentDuration - _processedDuration);

                VadSegment? lastNonFinalSegment = null;
                VadSegment? recognizingSegment  = null;

                await foreach ( var segment in vadDetector.DetectSegmentsAsync(slicedSource, cancellationToken) )
                {
                    if ( segment.IsIncomplete )
                    {
                        recognizingSegment = segment;

                        continue;
                    }

                    var segmentEnd = segment.StartTime + segment.Duration;
                    lastNonFinalSegment = segment;

                    logger.LogDebug("Processing VAD segment: Start={Start}ms, Duration={Duration}ms",
                                    segment.StartTime.TotalMilliseconds,
                                    segment.Duration.TotalMilliseconds);

                    var transcribeStopwatch = Stopwatch.StartNew();
                    var transcribingEvents = TranscribeSegments(
                                                                speechTranscriptorFactory,
                                                                source,
                                                                _processedDuration,
                                                                segment.StartTime,
                                                                segment.Duration,
                                                                promptBuilder,
                                                                detectedLanguage,
                                                                sessionId,
                                                                cancellationToken);

                    await foreach ( var segmentData in transcribingEvents )
                    {
                        if ( options.AutodetectLanguageOnce )
                        {
                            detectedLanguage = segmentData.Language;
                        }

                        if ( realtimeOptions.ConcatenateSegmentsToPrompt )
                        {
                            promptBuilder.Append(segmentData.Text);
                        }

                        logger.LogDebug(
                                        "Segment recognized: SessionId={SessionId}, Duration={Duration}ms, ProcessingTime={ProcessingTime}ms",
                                        sessionId,
                                        segmentData.Duration.TotalMilliseconds,
                                        transcribeStopwatch.ElapsedMilliseconds);

                        yield return new RealtimeSegmentRecognized(segmentData, sessionId);
                    }
                }

                if ( options.IncludeSpeechRecogizingEvents && recognizingSegment != null )
                {
                    logger.LogDebug("Processing recognizing segment: Duration={Duration}ms",
                                    recognizingSegment.Duration.TotalMilliseconds);

                    var transcribingEvents = TranscribeSegments(
                                                                recognizingSpeechTranscriptorFactory ?? speechTranscriptorFactory,
                                                                source,
                                                                _processedDuration,
                                                                recognizingSegment.StartTime,
                                                                recognizingSegment.Duration,
                                                                promptBuilder,
                                                                detectedLanguage,
                                                                sessionId,
                                                                cancellationToken);

                    await foreach ( var segment in transcribingEvents )
                    {
                        yield return new RealtimeSegmentRecognizing(segment, sessionId);
                    }
                }

                HandleSegmentProcessing(source, ref _processedDuration, lastNonFinalSegment, recognizingSegment, _lastDuration);

                logger.LogTrace("Segment processing completed in {ElapsedTime}ms",
                                segmentStopwatch.ElapsedMilliseconds);
            }

            var finalStopwatch = Stopwatch.StartNew();
            var lastEvents = TranscribeSegments(
                                                speechTranscriptorFactory,
                                                source,
                                                _processedDuration,
                                                TimeSpan.Zero,
                                                source.Duration - _processedDuration,
                                                promptBuilder,
                                                detectedLanguage,
                                                sessionId,
                                                cancellationToken);

            await foreach ( var segmentData in lastEvents )
            {
                if ( realtimeOptions.ConcatenateSegmentsToPrompt )
                {
                    promptBuilder.Append(segmentData.Text);
                }

                yield return new RealtimeSegmentRecognized(segmentData, sessionId);
            }

            logger.LogInformation(
                                  "Transcription session completed: SessionId={SessionId}, TotalDuration={TotalDuration}ms, TotalProcessingTime={TotalTime}ms",
                                  sessionId,
                                  source.Duration.TotalMilliseconds,
                                  stopwatch.ElapsedMilliseconds);

            yield return new RealtimeSessionStopped(sessionId);
        }
        finally
        {
            await CleanupSessionAsync(sessionId);
        }
    }

    private void HandleSegmentProcessing(
        IAudioSource source,
        ref TimeSpan processedDuration,
        VadSegment?  lastNonFinalSegment,
        VadSegment?  recognizingSegment,
        TimeSpan     lastDuration)
    {
        if ( lastNonFinalSegment != null )
        {
            var skippingDuration = lastNonFinalSegment.StartTime + lastNonFinalSegment.Duration;
            processedDuration += skippingDuration;

            if ( source is IDiscardableAudioSource discardableSource )
            {
                var lastSegmentEndFrameIndex = (int)(skippingDuration.TotalMilliseconds * source.SampleRate / 1000d) - 1;
                discardableSource.DiscardFrames(lastSegmentEndFrameIndex);
                logger.LogTrace("Discarded frames up to index {FrameIndex}", lastSegmentEndFrameIndex);
            }
        }
        else if ( recognizingSegment == null )
        {
            if ( lastDuration - processedDuration > realtimeOptions.SilenceDiscardInterval )
            {
                var silenceDurationToDiscard = TimeSpan.FromTicks(realtimeOptions.SilenceDiscardInterval.Ticks / 2);
                processedDuration += silenceDurationToDiscard;

                if ( source is IDiscardableAudioSource discardableSource )
                {
                    var halfSilenceIndex = (int)(silenceDurationToDiscard.TotalMilliseconds * source.SampleRate / 1000d) - 1;
                    discardableSource.DiscardFrames(halfSilenceIndex);
                    logger.LogTrace("Discarded silence frames up to index {FrameIndex}", halfSilenceIndex);
                }
            }
        }
    }

    private async IAsyncEnumerable<TranscriptSegment> TranscribeSegments(
        ISpeechTranscriptorFactory                 transcriptorFactory,
        IAudioSource                               source,
        TimeSpan                                   processedDuration,
        TimeSpan                                   startTime,
        TimeSpan                                   duration,
        StringBuilder                              promptBuilder,
        CultureInfo?                               detectedLanguage,
        string                                     sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if ( duration < realtimeOptions.MinTranscriptDuration )
        {
            yield break;
        }

        startTime += processedDuration;
        var paddedStart = startTime - realtimeOptions.PaddingDuration;

        if ( paddedStart < processedDuration )
        {
            paddedStart = processedDuration;
        }

        var paddedDuration = duration + realtimeOptions.PaddingDuration;

        using IAudioSource paddedSource = paddedDuration < realtimeOptions.MinDurationWithPadding
                                              ? GetSilenceAddedSource(source, paddedStart, paddedDuration)
                                              : new SliceAudioSource(source, paddedStart, paddedDuration);

        var languageAutodetect = options.LanguageAutoDetect;
        var language           = options.Language;

        if ( languageAutodetect && options.AutodetectLanguageOnce && detectedLanguage != null )
        {
            languageAutodetect = false;
            language           = detectedLanguage;
        }

        var currentOptions = options with { Prompt = realtimeOptions.ConcatenateSegmentsToPrompt ? promptBuilder.ToString() : options.Prompt, LanguageAutoDetect = languageAutodetect, Language = language };

        var transcriptor = GetOrCreateTranscriptor(transcriptorFactory, currentOptions, sessionId);

        await foreach ( var segment in transcriptor.TranscribeAsync(paddedSource, cancellationToken) )
        {
            segment.StartTime += processedDuration;

            yield return segment;
        }
    }

    private ISpeechTranscriptor GetOrCreateTranscriptor(
        ISpeechTranscriptorFactory        factory,
        RealtimeSpeechTranscriptorOptions currentOptions,
        string                            sessionId)
    {
        var cacheKey = $"{sessionId}_{factory.GetHashCode()}";

        lock (cacheLock)
        {
            if ( !transcriptorCache.TryGetValue(cacheKey, out var transcriptor) )
            {
                transcriptor = factory.Create(currentOptions);
                transcriptorCache.Add(cacheKey, transcriptor);
            }

            return transcriptor;
        }
    }

    private async ValueTask CleanupSessionAsync(string sessionId)
    {
        // lock (cacheLock)
        // {
        var keysToRemove = transcriptorCache.Keys
                                            .Where(k => k.StartsWith($"{sessionId}_"))
                                            .ToList();

        foreach ( var key in keysToRemove )
        {
            if ( !transcriptorCache.TryGetValue(key, out var transcriptor) )
            {
                continue;
            }

            await transcriptor.DisposeAsync();
            transcriptorCache.Remove(key);
        }
        // }
    }

    private ConcatAudioSource GetSilenceAddedSource(IAudioSource source, TimeSpan paddedStart, TimeSpan paddedDuration)
    {
        var silenceDuration = new TimeSpan((realtimeOptions.MinDurationWithPadding.Ticks - paddedDuration.Ticks) / 2);
        var preSilence      = new SilenceAudioSource(silenceDuration, source.SampleRate, source.Metadata, source.ChannelCount, source.BitsPerSample);
        var postSilence     = new SilenceAudioSource(silenceDuration, source.SampleRate, source.Metadata, source.ChannelCount, source.BitsPerSample);

        return new ConcatAudioSource([preSilence, new SliceAudioSource(source, paddedStart, paddedDuration), postSilence], source.Metadata);
    }
}