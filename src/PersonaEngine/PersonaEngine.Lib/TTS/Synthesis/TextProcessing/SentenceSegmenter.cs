using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace PersonaEngine.Lib.TTS.Synthesis.TextProcessing;

/// <summary>
///     High-performance sentence segmentation using both rule-based and ML approaches with
///     advanced handling of edge cases and text structures
/// </summary>
public partial class SentenceSegmenter(
    IMlSentenceDetector mlDetector,
    ILogger<SentenceSegmenter> logger
) : ISentenceSegmenter
{
    private const int MinimumSentences = 2;

    private static readonly Regex SpecialTokenPattern = new(
        @"\[[A-Z]+:[^\]]+\]",
        RegexOptions.Compiled
    );

    private readonly ILogger<SentenceSegmenter> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly IMlSentenceDetector _mlDetector =
        mlDetector ?? throw new ArgumentNullException(nameof(mlDetector));

    /// <summary>
    ///     Segments text into sentences with optimized handling and pre-processing for boundary detection
    /// </summary>
    public IReadOnlyList<string> Segment(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        try
        {
            // First attempt with original text
            var sentences = _mlDetector.Detect(text);

            // If we have fewer than minimum required sentences, try pre-processing
            if (sentences.Count < MinimumSentences)
            {
                _logger.LogTrace(
                    "Initial segmentation yielded only {count} sentence(s), attempting pre-processing",
                    sentences.Count
                );

                // Apply pre-processing to create potential sentence boundaries
                var preprocessedText = AddPotentialSentenceBoundaries(text);

                // Re-detect sentences with the pre-processed text
                sentences = _mlDetector.Detect(preprocessedText);

                _logger.LogTrace(
                    "After pre-processing, segmentation yielded {count} sentence(s)",
                    sentences.Count
                );
            }

            return sentences;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in sentence segmentation.");

            return [];
        }
    }

    /// <summary>
    ///     Adds potential sentence boundaries by replacing certain punctuation with periods
    ///     to help generate at least 2 sentences when possible
    /// </summary>
    private string AddPotentialSentenceBoundaries(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        try
        {
            if (!SpecialTokenPattern.IsMatch(text))
            {
                // No special tokens - apply normal processing
                return ApplyBasicPreprocessing(text);
            }

            var result = new StringBuilder(text.Length + 20);
            var currentPosition = 0;

            foreach (Match match in SpecialTokenPattern.Matches(text))
            {
                // Process the text between the last token and this one
                if (match.Index > currentPosition)
                {
                    var segment = text.Substring(currentPosition, match.Index - currentPosition);
                    result.Append(ApplyBasicPreprocessing(segment));
                }

                // Append the token unchanged
                result.Append(match.Value);
                currentPosition = match.Index + match.Length;
            }

            if (currentPosition < text.Length)
            {
                var segment = text[currentPosition..];
                result.Append(ApplyBasicPreprocessing(segment));
            }

            return result.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Error during preprocessing for sentence boundaries. Using original text."
            );

            return text;
        }
    }

    /// <summary>
    ///     Applies basic preprocessing rules to a text segment without special tokens
    /// </summary>
    private string ApplyBasicPreprocessing(string text)
    {
        // Replace semicolons with periods
        text = text.Replace(";", ".");

        // Replace colons with periods
        text = text.Replace(":", ".");

        // Replace dashes with periods (when surrounded by spaces)
        text = text.Replace(" - ", ". ");
        text = text.Replace(" – ", ". ");
        text = text.Replace(" — ", ". ");

        // Ensure proper spacing after periods for the ML detector
        text = TextAfterSpaceRegex().Replace(text, ". $1");

        return text;
    }

    [GeneratedRegex(@"\.([A-Za-z0-9])")]
    private static partial Regex TextAfterSpaceRegex();
}
