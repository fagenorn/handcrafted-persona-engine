using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenNLP.Tools.PosTagger;
using OpenNLP.Tools.Tokenize;

namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Implementation of OpenNLP-based POS tagger with spaCy-like currency handling
/// </summary>
public partial class OpenNlpPosTagger : IPosTagger
{
    private readonly Regex _currencySymbolRegex = CurrencyRegex();

    private readonly ILogger<OpenNlpPosTagger> _logger;

    private readonly EnglishMaximumEntropyPosTagger _posTagger;

    private readonly EnglishRuleBasedTokenizer _tokenizer;

    private bool _disposed;

    public OpenNlpPosTagger(string modelPath, ILogger<OpenNlpPosTagger> logger)
    {
        if (string.IsNullOrEmpty(modelPath))
        {
            throw new ArgumentException("Model path cannot be null or empty", nameof(modelPath));
        }

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        try
        {
            _tokenizer = new EnglishRuleBasedTokenizer(false);
            _posTagger = new EnglishMaximumEntropyPosTagger(modelPath);
            _logger.LogInformation("Initialized OpenNLP POS tagger from {ModelPath}", modelPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize OpenNLP POS tagger");

            throw;
        }
    }

    /// <summary>
    ///     Tags parts of speech in text using OpenNLP with spaCy-like currency handling
    /// </summary>
    public Task<IReadOnlyList<PosToken>> TagAsync(
        string text,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult<IReadOnlyList<PosToken>>(Array.Empty<PosToken>());
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Pre-process text to insert spaces between currency symbols and digits
            // This makes the tokenizer split currency symbols from amounts, similar to spaCy
            var currencyRegex = CurrencyWithNumRegex();
            var processedText = currencyRegex.Replace(text, "$1 $2");

            // Tokenize pre-processed text
            var tokens = _tokenizer.Tokenize(processedText);

            // Get POS tags
            var tags = _posTagger.Tag(tokens);

            // Post-process to assign "$" tag to currency symbols
            for (var i = 0; i < tokens.Length; i++)
            {
                if (_currencySymbolRegex.IsMatch(tokens[i]))
                {
                    tags[i] = "$"; // Assign "$" tag to currency symbols (spaCy-like behavior)
                }
            }

            // Build result tokens with whitespace
            var result = new List<PosToken>();
            var currentPosition = 0;

            for (var i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                var tag = tags[i];

                // Find token position in processed text
                var tokenPosition = processedText.IndexOf(
                    token,
                    currentPosition,
                    StringComparison.Ordinal
                );

                // Extract whitespace between tokens
                var whitespace = "";
                if (i < tokens.Length - 1)
                {
                    var nextTokenStart = processedText.IndexOf(
                        tokens[i + 1],
                        tokenPosition + token.Length,
                        StringComparison.Ordinal
                    );
                    if (nextTokenStart >= 0)
                    {
                        whitespace = processedText.Substring(
                            tokenPosition + token.Length,
                            nextTokenStart - (tokenPosition + token.Length)
                        );
                    }
                }
                else
                {
                    // Last token - get any remaining whitespace
                    whitespace =
                        tokenPosition + token.Length < processedText.Length
                            ? processedText[(tokenPosition + token.Length)..]
                            : "";
                }

                result.Add(
                    new PosToken
                    {
                        Text = token,
                        PartOfSpeech = tag,
                        IsWhitespace = whitespace.Contains(" "),
                    }
                );

                currentPosition = tokenPosition + token.Length;
            }

            return Task.FromResult<IReadOnlyList<PosToken>>(result);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("POS tagging was canceled");

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during POS tagging");

            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }

    [GeneratedRegex(@"([\$\€\£\¥\₹])(\d)")]
    private static partial Regex CurrencyWithNumRegex();

    [GeneratedRegex(@"^[\$\€\£\¥\₹]$")]
    private static partial Regex CurrencyRegex();
}
