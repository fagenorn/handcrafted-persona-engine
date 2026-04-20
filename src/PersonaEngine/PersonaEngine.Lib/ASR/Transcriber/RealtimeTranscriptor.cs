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
    private readonly ILogger<RealtimeTranscriptor> _logger;

    private readonly RealtimeSpeechTranscriptorOptions _options;

    private readonly RealtimeOptions _realtimeOptions;

    private readonly ISpeechTranscriptorFactory? _recognizingSpeechTranscriptorFactory;

    private readonly ISpeechTranscriptorFactory _speechTranscriptorFactory;

    private readonly IVadDetector _vadDetector;

    private bool _isDisposed;

    private TimeSpan _lastDuration = TimeSpan.Zero;

    private TimeSpan _processedDuration = TimeSpan.Zero;

    public RealtimeTranscriptor(
        ISpeechTranscriptorFactory speechTranscriptorFactory,
        IVadDetector vadDetector,
        ISpeechTranscriptorFactory? recognizingSpeechTranscriptorFactory,
        RealtimeSpeechTranscriptorOptions options,
        RealtimeOptions realtimeOptions,
        ILogger<RealtimeTranscriptor> logger
    )
    {
        _speechTranscriptorFactory = speechTranscriptorFactory;
        _vadDetector = vadDetector;
        _recognizingSpeechTranscriptorFactory = recognizingSpeechTranscriptorFactory;
        _options = options;
        _realtimeOptions = realtimeOptions;
        _logger = logger;
    }

    public ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return ValueTask.CompletedTask;
        }

        _isDisposed = true;

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<IRealtimeRecognitionEvent> TranscribeAsync(
        IAwaitableAudioSource source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var promptBuilder = new StringBuilder(_options.Prompt);
        CultureInfo? detectedLanguage = null;

        await source.WaitForInitializationAsync(cancellationToken);
        var sessionId = Guid.NewGuid().ToString();

        _logger.LogInformation("Starting transcription session {SessionId}", sessionId);

        yield return new RealtimeSessionStarted(sessionId);

        while (!source.IsFlushed)
        {
            var currentDuration = source.Duration;

            if (currentDuration == _lastDuration)
            {
                await source.WaitForNewSamplesAsync(
                    _lastDuration + _realtimeOptions.ProcessingInterval,
                    cancellationToken
                );

                continue;
            }

            _logger.LogTrace(
                "Processing new audio segment: Current={Current}ms, Last={Last}ms, Delta={Delta}ms",
                currentDuration.TotalMilliseconds,
                _lastDuration.TotalMilliseconds,
                (currentDuration - _lastDuration).TotalMilliseconds
            );

            _lastDuration = currentDuration;
            var slicedSource = new SliceAudioSource(
                source,
                _processedDuration,
                currentDuration - _processedDuration
            );

            VadSegment? lastNonFinalSegment = null;
            VadSegment? recognizingSegment = null;

            await foreach (
                var segment in _vadDetector.DetectSegmentsAsync(slicedSource, cancellationToken)
            )
            {
                if (segment.IsIncomplete)
                {
                    recognizingSegment = segment;

                    continue;
                }

                lastNonFinalSegment = segment;

                _logger.LogTrace(
                    "Processing VAD segment: Start={Start}ms, Duration={Duration}ms",
                    segment.StartTime.TotalMilliseconds,
                    segment.Duration.TotalMilliseconds
                );

                var transcribeStopwatch = Stopwatch.StartNew();
                var transcribingEvents = TranscribeSegments(
                    _speechTranscriptorFactory,
                    source,
                    _processedDuration,
                    segment.StartTime,
                    segment.Duration,
                    promptBuilder,
                    detectedLanguage,
                    cancellationToken
                );

                await foreach (var segmentData in transcribingEvents)
                {
                    if (_options.AutodetectLanguageOnce)
                    {
                        detectedLanguage = segmentData.Language;
                    }

                    if (_realtimeOptions.ConcatenateSegmentsToPrompt)
                    {
                        promptBuilder.Append(segmentData.Text);
                    }

                    _logger.LogDebug(
                        "Segment recognized: SessionId={SessionId}, Duration={Duration}ms, ProcessingTime={ProcessingTime}ms",
                        sessionId,
                        segmentData.Duration.TotalMilliseconds,
                        transcribeStopwatch.ElapsedMilliseconds
                    );

                    yield return new RealtimeSegmentRecognized(
                        segmentData,
                        sessionId,
                        transcribeStopwatch.Elapsed
                    );
                }
            }

            if (_options.IncludeSpeechRecogizingEvents && recognizingSegment != null)
            {
                _logger.LogDebug(
                    "Processing recognizing segment: Duration={Duration}ms",
                    recognizingSegment.Duration.TotalMilliseconds
                );

                var transcribeStopwatch = Stopwatch.StartNew();
                var transcribingEvents = TranscribeSegments(
                    _recognizingSpeechTranscriptorFactory ?? _speechTranscriptorFactory,
                    source,
                    _processedDuration,
                    recognizingSegment.StartTime,
                    recognizingSegment.Duration,
                    promptBuilder,
                    detectedLanguage,
                    cancellationToken
                );

                await foreach (var segment in transcribingEvents)
                {
                    yield return new RealtimeSegmentRecognizing(
                        segment,
                        sessionId,
                        transcribeStopwatch.Elapsed
                    );
                }
            }

            HandleSegmentProcessing(
                source,
                ref _processedDuration,
                lastNonFinalSegment,
                recognizingSegment,
                _lastDuration
            );
        }

        var transcribeStopwatchLast = Stopwatch.StartNew();
        var lastEvents = TranscribeSegments(
            _speechTranscriptorFactory,
            source,
            _processedDuration,
            TimeSpan.Zero,
            source.Duration - _processedDuration,
            promptBuilder,
            detectedLanguage,
            cancellationToken
        );

        await foreach (var segmentData in lastEvents)
        {
            if (_realtimeOptions.ConcatenateSegmentsToPrompt)
            {
                promptBuilder.Append(segmentData.Text);
            }

            yield return new RealtimeSegmentRecognized(
                segmentData,
                sessionId,
                transcribeStopwatchLast.Elapsed
            );
        }

        yield return new RealtimeSessionStopped(sessionId);
    }

    private void HandleSegmentProcessing(
        IAudioSource source,
        ref TimeSpan processedDuration,
        VadSegment? lastNonFinalSegment,
        VadSegment? recognizingSegment,
        TimeSpan lastDuration
    )
    {
        if (lastNonFinalSegment != null)
        {
            var skippingDuration = lastNonFinalSegment.StartTime + lastNonFinalSegment.Duration;
            processedDuration += skippingDuration;

            if (source is IDiscardableAudioSource discardableSource)
            {
                var lastSegmentEndFrameIndex =
                    (int)(skippingDuration.TotalMilliseconds * source.SampleRate / 1000d) - 1;

                discardableSource.DiscardFrames(lastSegmentEndFrameIndex);
                _logger.LogTrace(
                    "Discarded frames up to index {FrameIndex}",
                    lastSegmentEndFrameIndex
                );
            }
        }
        else if (recognizingSegment == null)
        {
            if (lastDuration - processedDuration > _realtimeOptions.SilenceDiscardInterval)
            {
                var silenceDurationToDiscard = TimeSpan.FromTicks(
                    _realtimeOptions.SilenceDiscardInterval.Ticks / 2
                );

                processedDuration += silenceDurationToDiscard;

                if (source is IDiscardableAudioSource discardableSource)
                {
                    var halfSilenceIndex =
                        (int)(
                            silenceDurationToDiscard.TotalMilliseconds * source.SampleRate / 1000d
                        ) - 1;

                    discardableSource.DiscardFrames(halfSilenceIndex);
                    _logger.LogTrace(
                        "Discarded silence frames up to index {FrameIndex}",
                        halfSilenceIndex
                    );
                }
            }
        }
    }

    private async IAsyncEnumerable<TranscriptSegment> TranscribeSegments(
        ISpeechTranscriptorFactory transcriptorFactory,
        IAudioSource source,
        TimeSpan processedDuration,
        TimeSpan startTime,
        TimeSpan duration,
        StringBuilder promptBuilder,
        CultureInfo? detectedLanguage,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (duration < _realtimeOptions.MinTranscriptDuration)
        {
            yield break;
        }

        startTime += processedDuration;
        var paddedStart = startTime - _realtimeOptions.PaddingDuration;

        if (paddedStart < processedDuration)
        {
            paddedStart = processedDuration;
        }

        var paddedDuration = duration + _realtimeOptions.PaddingDuration;

        using IAudioSource paddedSource =
            paddedDuration < _realtimeOptions.MinDurationWithPadding
                ? GetSilenceAddedSource(source, paddedStart, paddedDuration)
                : new SliceAudioSource(source, paddedStart, paddedDuration);

        var languageAutodetect = _options.LanguageAutoDetect;
        var language = _options.Language;

        if (languageAutodetect && _options.AutodetectLanguageOnce && detectedLanguage != null)
        {
            languageAutodetect = false;
            language = detectedLanguage;
        }

        var currentOptions = _options with
        {
            Prompt = _realtimeOptions.ConcatenateSegmentsToPrompt
                ? promptBuilder.ToString()
                : _options.Prompt,
            LanguageAutoDetect = languageAutodetect,
            Language = language,
        };

        await using var transcriptor = transcriptorFactory.Create(currentOptions);

        await foreach (var segment in transcriptor.TranscribeAsync(paddedSource, cancellationToken))
        {
            segment.StartTime += processedDuration;

            yield return segment;
        }
    }

    private ConcatAudioSource GetSilenceAddedSource(
        IAudioSource source,
        TimeSpan paddedStart,
        TimeSpan paddedDuration
    )
    {
        var silenceDuration = new TimeSpan(
            (_realtimeOptions.MinDurationWithPadding.Ticks - paddedDuration.Ticks) / 2
        );

        var preSilence = new SilenceAudioSource(
            silenceDuration,
            source.SampleRate,
            source.Metadata,
            source.ChannelCount,
            source.BitsPerSample
        );

        var postSilence = new SilenceAudioSource(
            silenceDuration,
            source.SampleRate,
            source.Metadata,
            source.ChannelCount,
            source.BitsPerSample
        );

        return new ConcatAudioSource(
            [preSilence, new SliceAudioSource(source, paddedStart, paddedDuration), postSilence],
            source.Metadata
        );
    }
}
