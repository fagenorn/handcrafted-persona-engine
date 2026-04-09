using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.LLM;
using PersonaEngine.Lib.TTS.Synthesis.Audio;
using PersonaEngine.Lib.TTS.Synthesis.LipSync;

namespace PersonaEngine.Lib.TTS.Synthesis.Engine;

/// <summary>
///     Processes a single sentence through the TTS pipeline: text filters →
///     phonemize → synthesize → post-process text filters → audio filters.
///     Holds reusable state (filter results, pipeline) that is cleared between
///     sentences to minimize allocations on the hot path.
/// </summary>
public sealed class SentenceProcessor
{
    private readonly List<ITextFilter> _textFilters;
    private readonly IPhonemizer _phonemizer;
    private readonly ILogger<SentenceProcessor> _logger;
    private readonly ILipSyncProcessorProvider? _lipSyncProvider;

    private readonly TextFilterResult[] _filterResults;
    private readonly AudioFilterPipeline _pipeline;

    public SentenceProcessor(
        IEnumerable<ITextFilter> textFilters,
        IEnumerable<IAudioFilter> audioFilters,
        IPhonemizer phonemizer,
        ILoggerFactory loggerFactory,
        ILipSyncProcessorProvider? lipSyncProvider = null
    )
    {
        _textFilters = textFilters.OrderByDescending(x => x.Priority).ToList();
        _phonemizer = phonemizer;
        _logger = loggerFactory.CreateLogger<SentenceProcessor>();
        _lipSyncProvider = lipSyncProvider;
        _filterResults = new TextFilterResult[_textFilters.Count];
        _pipeline = new AudioFilterPipeline(
            audioFilters.OrderByDescending(x => x.Priority).ToList()
        );
    }

    public async IAsyncEnumerable<AudioSegment> ProcessAsync(
        ISynthesisSession session,
        string sentence,
        bool isLastSegment,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var sentenceId = Guid.CreateVersion7();
        _pipeline.Reset();
        _lipSyncProvider?.Current.BeginSentence();

        // 1. Apply text filters
        var processedText = sentence;
        for (var i = 0; i < _textFilters.Count; i++)
        {
            var filterResult = await _textFilters[i]
                .ProcessAsync(processedText, cancellationToken)
                .ConfigureAwait(false);
            processedText = filterResult.ProcessedText;
            _filterResults[i] = filterResult;
        }

        // 2. Phonemize the fully-filtered text (always, unconditionally)
        var phonemeResult = await _phonemizer
            .ToPhonemesAsync(processedText, cancellationToken)
            .ConfigureAwait(false);

        // 3. Synthesize — engine applies timing to phonemeResult.Tokens
        await foreach (
            var segment in session
                .SynthesizeAsync(processedText, phonemeResult, isLastSegment, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            // 4. Post-process text filters
            for (var i = 0; i < _textFilters.Count; i++)
            {
                await _textFilters[i]
                    .PostProcessAsync(_filterResults[i], segment, cancellationToken)
                    .ConfigureAwait(false);
            }

            // 4.5: Enrich with lip sync (via currently active processor)
            var enriched = segment;
            if (_lipSyncProvider is not null)
            {
                var proc = _lipSyncProvider.Current;
                enriched = enriched with { LipSync = proc.Process(enriched) };
            }
            else
            {
                _logger.LogWarning("LipSync: _lipSyncProvider is null, skipping");
            }

            // 5. Stamp sentence ID and submit to audio filter pipeline
            var stamped = enriched with
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
}
