using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;
using PersonaEngine.Lib.LLM;

namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Main TTS engine implementation
/// </summary>
public class TtsEngine : ITtsEngine
{
    private readonly IList<IAudioFilter> _audioFilters;

    private readonly ILogger<TtsEngine> _logger;

    private readonly IOptionsMonitor<KokoroVoiceOptions> _options;

    private readonly IPhonemizer _phonemizer;

    private readonly IAudioSynthesizer _synthesizer;

    private readonly IList<ITextFilter> _textFilters;

    private readonly ITextProcessor _textProcessor;

    private readonly SemaphoreSlim _throttle;

    private bool _disposed;

    public TtsEngine(
        ITextProcessor                      textProcessor,
        IPhonemizer                         phonemizer,
        IAudioSynthesizer                   synthesizer,
        IOptionsMonitor<KokoroVoiceOptions> options,
        IEnumerable<IAudioFilter>           audioFilters,
        IEnumerable<ITextFilter>            textFilters,
        ILoggerFactory                      loggerFactory)
    {
        _textProcessor = textProcessor ?? throw new ArgumentNullException(nameof(textProcessor));
        _phonemizer    = phonemizer ?? throw new ArgumentNullException(nameof(phonemizer));
        _synthesizer   = synthesizer ?? throw new ArgumentNullException(nameof(synthesizer));
        _options       = options;
        _audioFilters  = audioFilters.OrderByDescending(x => x.Priority).ToList();
        _textFilters   = textFilters.OrderByDescending(x => x.Priority).ToList();
        _logger        = loggerFactory?.CreateLogger<TtsEngine>() ?? throw new ArgumentNullException(nameof(loggerFactory));

        _throttle = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
    }

    public void Dispose()
    {
        if ( _disposed )
        {
            return;
        }

        _throttle.Dispose();
        _disposed = true;
    }

    public async Task<CompletionReason> SynthesizeStreamingAsync(
        ChannelReader<LlmChunkEvent> inputReader,
        ChannelWriter<IOutputEvent>  outputWriter,
        Guid                         turnId,
        Guid                         sessionId,
        KokoroVoiceOptions?          options           = null,
        CancellationToken            cancellationToken = default
    )
    {
        var textBuffer      = new StringBuilder(4096);
        var completedReason = CompletionReason.Completed;
        var firstChunk      = true;

        try
        {
            await foreach ( var (_, _, _, textChunk) in inputReader.ReadAllAsync(cancellationToken).ConfigureAwait(false) )
            {
                if ( cancellationToken.IsCancellationRequested )
                {
                    break;
                }

                if ( string.IsNullOrEmpty(textChunk) )
                {
                    continue;
                }

                textBuffer.Append(textChunk);
                var currentText = textBuffer.ToString();

                var processedText = await _textProcessor.ProcessAsync(currentText, cancellationToken);
                var sentences     = processedText.Sentences;

                if ( sentences.Count <= 1 )
                {
                    continue;
                }

                // Process all sentences except the last one(which might be incomplete)
                for ( var i = 0; i < sentences.Count - 1; i++ )
                {
                    var sentence = sentences[i].Trim();
                    if ( string.IsNullOrWhiteSpace(sentence) )
                    {
                        continue;
                    }

                    await foreach ( var segment in ProcessSentenceAsync(sentence, options, cancellationToken) )
                    {
                        ApplyAudioFilters(segment);

                        if ( firstChunk )
                        {
                            var firstChunkEvent = new TtsStreamStartEvent(sessionId, turnId, DateTimeOffset.UtcNow);
                            await outputWriter.WriteAsync(firstChunkEvent, cancellationToken).ConfigureAwait(false);

                            firstChunk = false;
                        }

                        var chunkEvent = new TtsChunkEvent(sessionId, turnId, DateTimeOffset.UtcNow, segment);
                        await outputWriter.WriteAsync(chunkEvent, cancellationToken).ConfigureAwait(false);
                    }
                }

                textBuffer.Clear();
                textBuffer.Append(sentences[^1]);
            }

            var remainingText = textBuffer.ToString().Trim();
            if ( !string.IsNullOrEmpty(remainingText) )
            {
                await foreach ( var segment in ProcessSentenceAsync(remainingText, options, cancellationToken) )
                {
                    ApplyAudioFilters(segment);

                    if ( firstChunk )
                    {
                        var firstChunkEvent = new TtsStreamStartEvent(sessionId, turnId, DateTimeOffset.UtcNow);
                        await outputWriter.WriteAsync(firstChunkEvent, cancellationToken).ConfigureAwait(false);

                        firstChunk = false;
                    }
                    
                    var chunkEvent = new TtsChunkEvent(sessionId, turnId, DateTimeOffset.UtcNow, segment);
                    await outputWriter.WriteAsync(chunkEvent, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            completedReason = CompletionReason.Cancelled;
        }
        catch (Exception ex)
        {
            completedReason = CompletionReason.Error;

            await outputWriter.WriteAsync(new ErrorOutputEvent(sessionId, turnId, DateTimeOffset.UtcNow, ex), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if ( !firstChunk )
            {
                await outputWriter.WriteAsync(new TtsStreamEndEvent(sessionId, turnId, DateTimeOffset.UtcNow, completedReason), cancellationToken).ConfigureAwait(false);
            }
        }

        return completedReason;
    }

    /// <summary>
    ///     Processes a single sentence for synthesis
    /// </summary>
    private async IAsyncEnumerable<AudioSegment> ProcessSentenceAsync(
        string                                     sentence,
        KokoroVoiceOptions?                        options           = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if ( string.IsNullOrWhiteSpace(sentence) )
        {
            yield break;
        }

        await _throttle.WaitAsync(cancellationToken);

        var currentOptions = options ?? _options.CurrentValue;

        try
        {
            // Apply text filters before processing
            var processedText     = sentence;
            var textFilterResults = new List<TextFilterResult>(_textFilters.Count);

            foreach ( var textFilter in _textFilters )
            {
                var filterResult = await textFilter.ProcessAsync(processedText, cancellationToken);
                processedText = filterResult.ProcessedText;

                textFilterResults.Add(filterResult);
            }

            // Get phonemes
            var phonemeResult = await _phonemizer.ToPhonemesAsync(processedText, cancellationToken);

            // Process each phoneme chunk
            foreach ( var phonemeChunk in SplitPhonemes(phonemeResult.Phonemes, 510) )
            {
                // Get audio
                var audioData = await _synthesizer.SynthesizeAsync(
                                                                   phonemeChunk,
                                                                   currentOptions,
                                                                   cancellationToken);

                // Apply timing
                ApplyTokenTimings(
                                  phonemeResult.Tokens,
                                  currentOptions,
                                  audioData.PhonemeTimings);

                // Return segment
                var segment = new AudioSegment(
                                               audioData.Samples,
                                               options?.SampleRate ?? _options.CurrentValue.SampleRate,
                                               phonemeResult.Tokens);

                for ( var index = 0; index < _textFilters.Count; index++ )
                {
                    var textFilter       = _textFilters[index];
                    var textFilterResult = textFilterResults[index];
                    await textFilter.PostProcessAsync(textFilterResult, segment, cancellationToken);
                }

                yield return segment;
            }
        }
        finally
        {
            _throttle.Release();
        }
    }

    /// <summary>
    ///     Splits phonemes into manageable chunks
    /// </summary>
    private IEnumerable<string> SplitPhonemes(string phonemes, int maxLength)
    {
        if ( string.IsNullOrEmpty(phonemes) )
        {
            yield return string.Empty;

            yield break;
        }

        if ( phonemes.Length <= maxLength )
        {
            yield return phonemes;

            yield break;
        }

        var currentIndex = 0;
        while ( currentIndex < phonemes.Length )
        {
            var remainingLength = phonemes.Length - currentIndex;
            var chunkSize       = Math.Min(maxLength, remainingLength);

            // Find a good breakpoint (whitespace or punctuation)
            if ( chunkSize < remainingLength && chunkSize > 10 )
            {
                // Look backwards from the end to find a good breakpoint
                for ( var i = currentIndex + chunkSize - 1; i > currentIndex + 10; i-- )
                {
                    if ( char.IsWhiteSpace(phonemes[i]) || IsPunctuation(phonemes[i]) )
                    {
                        chunkSize = i - currentIndex + 1;

                        break;
                    }
                }
            }

            yield return phonemes.Substring(currentIndex, chunkSize);
            currentIndex += chunkSize;
        }
    }

    private bool IsPunctuation(char c) { return ".,:;!?-—()[]{}\"'".Contains(c); }

    private void ApplyAudioFilters(AudioSegment audioSegment)
    {
        foreach ( var audioFilter in _audioFilters )
        {
            audioFilter.Process(audioSegment);
        }
    }

    /// <summary>
    ///     Applies timing information to tokens
    /// </summary>
    private void ApplyTokenTimings(
        IReadOnlyList<Token> tokens,
        KokoroVoiceOptions   options,
        ReadOnlyMemory<long> phonemeTimings)
    {
        // Skip if no timing information
        if ( tokens.Count == 0 || phonemeTimings.Length < 3 )
        {
            return;
        }

        var timingsSpan = phonemeTimings.Span;

        // Magic scaling factor for timing conversion
        const int TIME_DIVISOR = 80;

        // Start with boundary tokens (often <bos> token)
        var leftTime  = options.TrimSilence ? 0 : 2 * Math.Max(0, timingsSpan[0] - 3);
        var rightTime = leftTime;

        // Process each token
        var timingIndex = 1;
        foreach ( var token in tokens )
        {
            // Skip tokens without phonemes
            if ( string.IsNullOrEmpty(token.Phonemes) )
            {
                // Handle whitespace timing specially
                if ( token.Whitespace == " " && timingIndex + 1 < timingsSpan.Length )
                {
                    timingIndex++;
                    leftTime  = rightTime + timingsSpan[timingIndex];
                    rightTime = leftTime + timingsSpan[timingIndex];
                    timingIndex++;
                }

                continue;
            }

            // Calculate end index for this token's phonemes
            var endIndex = timingIndex + (token.Phonemes?.Length ?? 0);
            if ( endIndex >= phonemeTimings.Length )
            {
                continue;
            }

            // Start time for this token
            var startTime = (double)leftTime / TIME_DIVISOR;

            // Sum durations for all phonemes in this token
            var tokenDuration = 0L;
            for ( var i = timingIndex; i < endIndex && i < timingsSpan.Length; i++ )
            {
                tokenDuration += timingsSpan[i];
            }

            // Handle whitespace after token
            var spaceDuration = token.Whitespace == " " && endIndex < timingsSpan.Length
                                    ? timingsSpan[endIndex]
                                    : 0;

            // Calculate end time
            leftTime = rightTime + 2 * tokenDuration + spaceDuration;
            var endTime = (double)leftTime / TIME_DIVISOR;
            rightTime = leftTime + spaceDuration;

            // Add token with timing
            token.StartTs = startTime;
            token.EndTs   = endTime;

            // Move to next token's timing
            timingIndex = endIndex + (token.Whitespace == " " ? 1 : 0);
        }
    }
}