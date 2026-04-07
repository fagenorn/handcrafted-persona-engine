using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.LLM;
using PersonaEngine.Lib.TTS.Synthesis.Audio;
using PersonaEngine.Lib.Utils;

namespace PersonaEngine.Lib.TTS.Synthesis.Engine;

/// <summary>
///     Processes a single sentence through the TTS pipeline: text filters → emotion
///     marker strip → phonemize → synthesize → enrich tokens → audio filters.
///     Holds reusable state (dictionary, filter results, pipeline) that is cleared
///     between sentences to minimize allocations on the hot path.
/// </summary>
public sealed partial class SentenceProcessor
{
    [GeneratedRegex(@"\[__EM\d+__\]\(//\)")]
    private static partial Regex EmotionMarkerRegex();

    private readonly List<ITextFilter> _textFilters;
    private readonly List<IAudioFilter> _audioFilters;
    private readonly IPhonemizer _phonemizer;
    private readonly ILogger<SentenceProcessor> _logger;

    // Reusable per-sentence state — cleared at the start of each ProcessAsync call.
    // Safe because synthesis is single-threaded per turn.
    private readonly TextFilterResult[] _filterResults;
    private readonly Dictionary<string, Token> _phonemeLookup;
    private readonly AudioFilterPipeline _pipeline;

    public SentenceProcessor(
        IEnumerable<ITextFilter> textFilters,
        IEnumerable<IAudioFilter> audioFilters,
        IPhonemizer phonemizer,
        ILoggerFactory loggerFactory
    )
    {
        _textFilters = textFilters.OrderByDescending(x => x.Priority).ToList();
        _audioFilters = audioFilters.OrderByDescending(x => x.Priority).ToList();
        _phonemizer = phonemizer;
        _logger = loggerFactory.CreateLogger<SentenceProcessor>();
        _filterResults = new TextFilterResult[_textFilters.Count];
        _phonemeLookup = new Dictionary<string, Token>(64, StringComparer.OrdinalIgnoreCase);
        _pipeline = new AudioFilterPipeline(_audioFilters);
    }

    public async IAsyncEnumerable<AudioSegment> ProcessAsync(
        ISynthesisSession session,
        TtsEngineCapabilities capabilities,
        string sentence,
        bool isLastSegment,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var sentenceId = Guid.CreateVersion7();

        // Reset reusable state
        _phonemeLookup.Clear();
        _pipeline.Reset();

        // Apply text filters
        var processedText = sentence;
        for (var i = 0; i < _textFilters.Count; i++)
        {
            var filterResult = await _textFilters[i]
                .ProcessAsync(processedText, cancellationToken)
                .ConfigureAwait(false);
            processedText = filterResult.ProcessedText;
            _filterResults[i] = filterResult;
        }

        // Strip emotion markers for engines that don't handle them natively
        if (!capabilities.HasFlag(TtsEngineCapabilities.ProvidesPhonemes))
        {
            processedText = EmotionMarkerRegex().Replace(processedText, string.Empty);
        }

        // Run phonemizer on the original sentence for enrichment (if engine doesn't provide phonemes)
        PhonemeResult? phonemeResult = null;
        if (!capabilities.HasFlag(TtsEngineCapabilities.ProvidesPhonemes))
        {
            phonemeResult = await _phonemizer
                .ToPhonemesAsync(sentence, cancellationToken)
                .ConfigureAwait(false);
        }

        // Synthesize via session, applying audio filters through the pipeline
        await foreach (
            var segment in session
                .SynthesizeAsync(processedText, isLastSegment, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            // Enrich tokens with phoneme data if the engine doesn't provide them
            if (phonemeResult is not null)
            {
                EnrichTokensWithPhonemes(segment.Tokens, phonemeResult.Tokens);
            }

            // Post-process text filters
            for (var i = 0; i < _textFilters.Count; i++)
            {
                await _textFilters[i]
                    .PostProcessAsync(_filterResults[i], segment, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Stamp sentence ID and submit to audio filter pipeline
            var stamped = segment with
            {
                SentenceId = sentenceId,
            };
            foreach (var processed in _pipeline.Submit(stamped))
            {
                yield return processed;
            }
        }

        // Flush any remaining buffered audio
        foreach (var remaining in _pipeline.Flush())
        {
            yield return remaining;
        }
    }

    /// <summary>
    ///     Merges phoneme data from the phonemizer's token output into the engine's tokens
    ///     by matching on word text. Uses the reusable <see cref="_phonemeLookup" /> dictionary.
    /// </summary>
    private void EnrichTokensWithPhonemes(
        IReadOnlyList<Token> engineTokens,
        IReadOnlyList<Token> phonemizerTokens
    )
    {
        _phonemeLookup.Clear();

        foreach (var pt in phonemizerTokens)
        {
            if (!string.IsNullOrEmpty(pt.Text) && !string.IsNullOrEmpty(pt.Phonemes))
            {
                _phonemeLookup.TryAdd(pt.Text, pt);

                var stripped = pt.Text.TrimPunctuation();
                if (stripped.Length > 0 && !stripped.Equals(pt.Text, StringComparison.Ordinal))
                {
                    _phonemeLookup.TryAdd(stripped, pt);
                }
            }
        }

        var spanLookup = _phonemeLookup.GetAlternateLookup<ReadOnlySpan<char>>();

        foreach (var token in engineTokens)
        {
            if (!string.IsNullOrEmpty(token.Phonemes) || string.IsNullOrEmpty(token.Text))
            {
                continue;
            }

            if (spanLookup.TryGetValue(token.Text.AsSpan(), out var match))
            {
                token.Phonemes = match.Phonemes;
                token.Tag = match.Tag;
                token.Stress = match.Stress;
            }
            else
            {
                var stripped = token.Text.AsSpan().TrimPunctuationSpan();
                if (
                    stripped.Length > 0
                    && !stripped.SequenceEqual(token.Text.AsSpan())
                    && spanLookup.TryGetValue(stripped, out match)
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
