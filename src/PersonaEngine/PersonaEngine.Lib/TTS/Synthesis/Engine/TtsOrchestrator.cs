using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;
using PersonaEngine.Lib.LLM;
using PersonaEngine.Lib.TTS.Synthesis.Audio;
using PersonaEngine.Lib.TTS.Synthesis.TextProcessing;
using PersonaEngine.Lib.Utils;

namespace PersonaEngine.Lib.TTS.Synthesis.Engine;

/// <summary>
///     Engine-agnostic TTS orchestrator. Reads LLM chunks, segments sentences,
///     applies text/audio filters, delegates synthesis to the active <see cref="ISentenceSynthesizer" />,
///     and emits TTS lifecycle events.
/// </summary>
public sealed class TtsOrchestrator : ITtsEngine
{
    private static readonly Regex EmotionMarkerRegex = new(
        @"\[__EM\d+__\]\(//\)",
        RegexOptions.Compiled
    );

    private readonly IList<IAudioFilter> _audioFilters;
    private readonly ITtsEngineProvider _engineProvider;
    private readonly ILogger<TtsOrchestrator> _logger;
    private readonly IPhonemizer _phonemizer;
    private readonly IList<ITextFilter> _textFilters;
    private readonly ITextProcessor _textProcessor;

    public TtsOrchestrator(
        ITextProcessor textProcessor,
        ITtsEngineProvider engineProvider,
        IPhonemizer phonemizer,
        IEnumerable<IAudioFilter> audioFilters,
        IEnumerable<ITextFilter> textFilters,
        ILoggerFactory loggerFactory
    )
    {
        _textProcessor = textProcessor ?? throw new ArgumentNullException(nameof(textProcessor));
        _engineProvider = engineProvider ?? throw new ArgumentNullException(nameof(engineProvider));
        _phonemizer = phonemizer ?? throw new ArgumentNullException(nameof(phonemizer));
        _audioFilters = audioFilters.OrderByDescending(x => x.Priority).ToList();
        _textFilters = textFilters.OrderByDescending(x => x.Priority).ToList();
        _logger =
            loggerFactory.CreateLogger<TtsOrchestrator>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public void Dispose() { }

    public async Task<CompletionReason> SynthesizeStreamingAsync(
        ChannelReader<LlmChunkEvent> inputReader,
        ChannelWriter<IOutputEvent> outputWriter,
        Guid turnId,
        Guid sessionId,
        CancellationToken cancellationToken = default
    )
    {
        var textBuffer = new StringBuilder(4096);
        var completedReason = CompletionReason.Completed;
        var firstChunk = true;
        var primed = true;

        var synthesizer = _engineProvider.Current;
        _logger.LogDebug(
            "TTS orchestrator: starting with engine '{EngineId}' for turn {TurnId}",
            synthesizer.EngineId,
            turnId
        );

        ISynthesisSession? session = null;

        try
        {
            _logger.LogDebug(
                "TTS orchestrator: creating synthesis session (engine '{EngineId}')",
                synthesizer.EngineId
            );
            session = synthesizer.CreateSession();
            _logger.LogDebug("TTS orchestrator: synthesis session created");

            await foreach (
                var (_, _, _, textChunk) in inputReader
                    .ReadAllAsync(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (string.IsNullOrEmpty(textChunk))
                {
                    continue;
                }

                textBuffer.Append(textChunk);
                var currentText = textBuffer.ToString();

                var processedText = await _textProcessor.ProcessAsync(
                    currentText,
                    cancellationToken
                );
                var sentences = processedText.Sentences;

                if (sentences.Count <= 1)
                {
                    continue;
                }

                _logger.LogDebug(
                    "TTS orchestrator: detected {Count} sentences, synthesizing {Ready} completed",
                    sentences.Count,
                    sentences.Count - 1
                );

                // Completed sentences (all but the last, which is still accumulating)
                for (var i = 0; i < sentences.Count - 1; i++)
                {
                    var sentence = sentences[i].Trim();
                    if (string.IsNullOrWhiteSpace(sentence))
                    {
                        continue;
                    }

                    if (primed)
                    {
                        var primedEvent = new TtsReadyToSynthesizeEvent(
                            sessionId,
                            turnId,
                            DateTimeOffset.UtcNow
                        );
                        await outputWriter
                            .WriteAsync(primedEvent, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    _logger.LogDebug(
                        "TTS orchestrator: synthesizing sentence ({Len} chars): \"{Preview}\"",
                        sentence.Length,
                        sentence.Length > 80 ? sentence[..80] + "..." : sentence
                    );

                    // Mid-turn sentences are never the last segment
                    await foreach (
                        var segment in ProcessSentenceAsync(
                            session,
                            synthesizer,
                            sentence,
                            isLastSegment: false,
                            cancellationToken
                        )
                    )
                    {
                        if (firstChunk)
                        {
                            var firstChunkEvent = new TtsStreamStartEvent(
                                sessionId,
                                turnId,
                                DateTimeOffset.UtcNow
                            );
                            await outputWriter
                                .WriteAsync(firstChunkEvent, cancellationToken)
                                .ConfigureAwait(false);
                            firstChunk = false;
                        }

                        var chunkEvent = new TtsChunkEvent(
                            sessionId,
                            turnId,
                            DateTimeOffset.UtcNow,
                            segment
                        );
                        await outputWriter
                            .WriteAsync(chunkEvent, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                textBuffer.Clear();
                textBuffer.Append(sentences[^1]);
            }

            // Final remaining text — this is the last segment in the turn
            var remainingText = textBuffer.ToString().Trim();
            if (!string.IsNullOrEmpty(remainingText))
            {
                _logger.LogDebug(
                    "TTS orchestrator: synthesizing final segment ({Len} chars): \"{Preview}\"",
                    remainingText.Length,
                    remainingText.Length > 80 ? remainingText[..80] + "..." : remainingText
                );

                if (primed)
                {
                    var primedEvent = new TtsReadyToSynthesizeEvent(
                        sessionId,
                        turnId,
                        DateTimeOffset.UtcNow
                    );
                    await outputWriter
                        .WriteAsync(primedEvent, cancellationToken)
                        .ConfigureAwait(false);
                }

                await foreach (
                    var segment in ProcessSentenceAsync(
                        session,
                        synthesizer,
                        remainingText,
                        isLastSegment: true,
                        cancellationToken
                    )
                )
                {
                    if (firstChunk)
                    {
                        var firstChunkEvent = new TtsStreamStartEvent(
                            sessionId,
                            turnId,
                            DateTimeOffset.UtcNow
                        );
                        await outputWriter
                            .WriteAsync(firstChunkEvent, cancellationToken)
                            .ConfigureAwait(false);
                        firstChunk = false;
                    }

                    var chunkEvent = new TtsChunkEvent(
                        sessionId,
                        turnId,
                        DateTimeOffset.UtcNow,
                        segment
                    );
                    await outputWriter
                        .WriteAsync(chunkEvent, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                _logger.LogDebug("TTS orchestrator: no remaining text after LLM stream ended");
            }
        }
        catch (OperationCanceledException)
        {
            completedReason = CompletionReason.Cancelled;
            _logger.LogDebug("TTS orchestrator: cancelled for turn {TurnId}", turnId);
        }
        catch (Exception ex)
        {
            completedReason = CompletionReason.Error;
            _logger.LogError(
                ex,
                "TTS orchestrator: error during synthesis for turn {TurnId} (engine '{EngineId}')",
                turnId,
                synthesizer.EngineId
            );

            try
            {
                await outputWriter
                    .WriteAsync(
                        new ErrorOutputEvent(sessionId, turnId, DateTimeOffset.UtcNow, ex),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception writeEx)
            {
                _logger.LogError(
                    writeEx,
                    "TTS orchestrator: failed to write ErrorOutputEvent for turn {TurnId}",
                    turnId
                );
            }
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            if (!firstChunk)
            {
                await outputWriter
                    .WriteAsync(
                        new TtsStreamEndEvent(
                            sessionId,
                            turnId,
                            DateTimeOffset.UtcNow,
                            completedReason
                        ),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            else if (completedReason == CompletionReason.Error)
            {
                // No TTS chunks were ever emitted but we errored — still emit
                // TtsStreamEnd so the state machine doesn't hang in StreamingResponse.
                _logger.LogWarning(
                    "TTS orchestrator: error before any audio was produced for turn {TurnId}, emitting TtsStreamEnd",
                    turnId
                );

                await outputWriter
                    .WriteAsync(
                        new TtsStreamEndEvent(
                            sessionId,
                            turnId,
                            DateTimeOffset.UtcNow,
                            completedReason
                        ),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
        }

        _logger.LogDebug(
            "TTS orchestrator: finished with result {Result} for turn {TurnId}",
            completedReason,
            turnId
        );

        return completedReason;
    }

    private async IAsyncEnumerable<AudioSegment> ProcessSentenceAsync(
        ISynthesisSession session,
        ISentenceSynthesizer synthesizer,
        string sentence,
        bool isLastSegment,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var sentenceId = Guid.NewGuid();

        // Apply text filters
        var processedText = sentence;
        var textFilterResults = new List<TextFilterResult>(_textFilters.Count);

        foreach (var textFilter in _textFilters)
        {
            var filterResult = await textFilter.ProcessAsync(processedText, cancellationToken);
            processedText = filterResult.ProcessedText;
            textFilterResults.Add(filterResult);
        }

        // Strip emotion markers for engines that don't handle them natively
        if (!synthesizer.Capabilities.HasFlag(TtsEngineCapabilities.ProvidesPhonemes))
        {
            processedText = EmotionMarkerRegex.Replace(processedText, string.Empty);
        }

        // Run phonemizer on the original sentence for enrichment (if engine doesn't provide phonemes)
        PhonemeResult? phonemeResult = null;
        if (!synthesizer.Capabilities.HasFlag(TtsEngineCapabilities.ProvidesPhonemes))
        {
            phonemeResult = await _phonemizer.ToPhonemesAsync(sentence, cancellationToken);
        }

        // Synthesize via session, applying audio filters through the pipeline
        var pipeline = new AudioFilterPipeline(_audioFilters);

        await foreach (
            var segment in session.SynthesizeAsync(processedText, isLastSegment, cancellationToken)
        )
        {
            // Enrich tokens with phoneme data if the engine doesn't provide them
            if (phonemeResult is not null)
            {
                EnrichTokensWithPhonemes(segment.Tokens, phonemeResult.Tokens);
            }

            // Post-process text filters
            for (var index = 0; index < _textFilters.Count; index++)
            {
                var textFilter = _textFilters[index];
                var textFilterResult = textFilterResults[index];
                await textFilter.PostProcessAsync(textFilterResult, segment, cancellationToken);
            }

            // Stamp sentence ID and submit to audio filter pipeline
            var stamped = segment with
            {
                SentenceId = sentenceId,
            };
            foreach (var processed in pipeline.Submit(stamped))
            {
                yield return processed;
            }
        }

        // Flush any remaining buffered audio
        foreach (var remaining in pipeline.Flush())
        {
            yield return remaining;
        }
    }

    /// <summary>
    ///     Merges phoneme data from the phonemizer's token output into the engine's tokens
    ///     by matching on word text. This allows engines that don't produce phonemes natively
    ///     (e.g., Qwen3) to provide lip-sync data.
    /// </summary>
    private static void EnrichTokensWithPhonemes(
        IReadOnlyList<Token> engineTokens,
        IReadOnlyList<Token> phonemizerTokens
    )
    {
        // Build lookups from word text → phoneme data (first match wins).
        // We index by both raw text and punctuation-stripped text because the
        // engine (Qwen3) splits by whitespace ("Hello,", "you?") while the
        // phonemizer tokenizes words without trailing punctuation ("Hello", "you").
        var phonemeLookup = new Dictionary<string, Token>(
            phonemizerTokens.Count * 2,
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var pt in phonemizerTokens)
        {
            if (!string.IsNullOrEmpty(pt.Text) && !string.IsNullOrEmpty(pt.Phonemes))
            {
                phonemeLookup.TryAdd(pt.Text, pt);

                var stripped = pt.Text.TrimPunctuation();
                if (stripped.Length > 0 && stripped != pt.Text)
                {
                    phonemeLookup.TryAdd(stripped, pt);
                }
            }
        }

        foreach (var token in engineTokens)
        {
            if (!string.IsNullOrEmpty(token.Phonemes) || string.IsNullOrEmpty(token.Text))
            {
                continue;
            }

            if (phonemeLookup.TryGetValue(token.Text, out var match))
            {
                token.Phonemes = match.Phonemes;
                token.Tag = match.Tag;
                token.Stress = match.Stress;
            }
            else
            {
                var stripped = token.Text.TrimPunctuation();
                if (
                    stripped.Length > 0
                    && stripped != token.Text
                    && phonemeLookup.TryGetValue(stripped, out match)
                )
                {
                    token.Phonemes = match.Phonemes;
                    token.Tag = match.Tag;
                    token.Stress = match.Stress;
                }
            }
        }
    }

}
