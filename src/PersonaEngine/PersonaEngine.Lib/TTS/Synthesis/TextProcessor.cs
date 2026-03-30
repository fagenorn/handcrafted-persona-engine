using Microsoft.Extensions.Logging;

namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Implementation of text processing for TTS
/// </summary>
public class TextProcessor : ITextProcessor
{
    private readonly ILogger<TextProcessor> _logger;

    private readonly ITextNormalizer _normalizer;

    private readonly ISentenceSegmenter _segmenter;

    public TextProcessor(
        ITextNormalizer normalizer,
        ISentenceSegmenter segmenter,
        ILogger<TextProcessor> logger
    )
    {
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _segmenter = segmenter ?? throw new ArgumentNullException(nameof(segmenter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Processes text for TTS by normalizing and segmenting into sentences
    /// </summary>
    public Task<ProcessedText> ProcessAsync(
        string text,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(text))
        {
            _logger.LogInformation("Empty text received for processing");

            return Task.FromResult(new ProcessedText(string.Empty, Array.Empty<string>()));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Normalize the text
            var normalizedText = _normalizer.Normalize(text);

            if (string.IsNullOrEmpty(normalizedText))
            {
                _logger.LogWarning("Text normalization resulted in empty text");

                return Task.FromResult(new ProcessedText(string.Empty, Array.Empty<string>()));
            }

            // Segment into sentences
            var sentences = _segmenter.Segment(normalizedText);

            _logger.LogDebug("Processed text into {SentenceCount} sentences", sentences.Count);

            return Task.FromResult(new ProcessedText(normalizedText, sentences));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing text");

            throw;
        }
    }
}
