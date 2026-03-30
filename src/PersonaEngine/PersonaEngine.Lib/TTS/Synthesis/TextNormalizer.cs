using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Normalizes text for better TTS pronunciation
/// </summary>
public class TextNormalizer : ITextNormalizer
{
    // Whitespace normalization
    private static readonly Regex NonStandardWhitespaceRegex = new(
        @"[^\S \n]",
        RegexOptions.Compiled
    );

    private static readonly Regex MultipleSpacesRegex = new(@" {2,}", RegexOptions.Compiled);

    private static readonly Regex SpaceBeforePunctuationRegex = new(
        @"\s+([.,;:?!])",
        RegexOptions.Compiled
    );

    // Quotation marks
    private static readonly Regex CurlyQuotesRegex = new(
        @"[\u2018\u2019\u201C\u201D]",
        RegexOptions.Compiled
    );

    // Abbreviations
    private static readonly Dictionary<Regex, string> CommonAbbreviations = new()
    {
        {
            new Regex(@"\bDr\.(?=\s+[A-Z])", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "Doctor"
        },
        {
            new Regex(@"\bMr\.(?=\s+[A-Z])", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "Mister"
        },
        {
            new Regex(@"\bMs\.(?=\s+[A-Z])", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "Miss"
        },
        {
            new Regex(@"\bMrs\.(?=\s+[A-Z])", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "Missus"
        },
        {
            new Regex(@"\betc\.(?!\s+[A-Z])", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "etcetera"
        },
        { new Regex(@"\bSt\.(?=\s+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Street" },
        { new Regex(@"\bAve\.(?=\s+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Avenue" },
        { new Regex(@"\bRd\.(?=\s+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Road" },
        {
            new Regex(@"\bPh\.D\.(?=\s+|\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "PhD"
        },
    };

    // Number regex for finding numeric patterns
    private static readonly Regex NumberRegex = new(
        @"(?<!\w)-?\d+(\.\d+)?%?",
        RegexOptions.Compiled
    );

    // URLs and email addresses
    private static readonly Regex UrlRegex = new(
        @"https?://[\w.-]+(?:/[\w.-]*)*/?",
        RegexOptions.Compiled
    );

    private static readonly Regex EmailRegex = new(
        @"[\w.+-]+@[\w.-]+\.\w{2,}",
        RegexOptions.Compiled
    );

    private readonly ILogger<TextNormalizer> _logger;

    public TextNormalizer(ILogger<TextNormalizer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Normalizes text for TTS synthesis
    /// </summary>
    public string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        try
        {
            text = text.Normalize(NormalizationForm.FormC);

            text = CleanupLines(text);

            text = NormalizeCharacters(text);

            text = ProcessUrlsAndEmails(text);

            text = ExpandAbbreviations(text);

            text = NormalizeWhitespaceAndPunctuation(text);

            text = RemoveInvalidSurrogates(text);

            return text.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during text normalization");

            return text;
        }
    }

    private string CleanupLines(string text)
    {
        // Split by line, trim each line, and remove empty ones
        var lines = text.Split('\n');
        var nonEmptyLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (!string.IsNullOrEmpty(trimmedLine))
            {
                nonEmptyLines.Add(trimmedLine);
            }
        }

        return string.Join("\n", nonEmptyLines);
    }

    private string NormalizeCharacters(string text)
    {
        // Convert curly quotes to straight quotes
        text = CurlyQuotesRegex.Replace(
            text,
            match =>
            {
                return match.Value switch
                {
                    "\u2018" or "\u2019" => "'",
                    "\u201C" or "\u201D" => "\"",
                    _ => match.Value,
                };
            }
        );

        // Replace other special characters
        text = text.Replace("…", "...")
            .Replace("–", "-")
            .Replace("—", " - ")
            .Replace("•", "*")
            .Replace("®", " registered ")
            .Replace("©", " copyright ")
            .Replace("™", " trademark ");

        // Convert Unicode fractions to text
        text = text.Replace("½", " one half ")
            .Replace("¼", " one quarter ")
            .Replace("¾", " three quarters ");

        return text;
    }

    private string RemoveInvalidSurrogates(string input)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < input.Length; i++)
        {
            if (char.IsHighSurrogate(input[i]))
            {
                // Check if there's a valid low surrogate following
                if (i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
                {
                    sb.Append(input[i]);
                    sb.Append(input[i + 1]);
                    i++; // Skip the next character as we've already handled it
                }
                // Otherwise, skip the invalid high surrogate
            }
            else if (!char.IsLowSurrogate(input[i]))
            {
                // Keep normal characters, skip orphaned low surrogates
                sb.Append(input[i]);
            }
        }

        return sb.ToString();
    }

    private bool IsValidUtf16(string input)
    {
        for (var i = 0; i < input.Length; i++)
        {
            if (char.IsHighSurrogate(input[i]))
            {
                if (i + 1 >= input.Length || !char.IsLowSurrogate(input[i + 1]))
                {
                    return false;
                }

                i++; // Skip the low surrogate
            }
            else if (char.IsLowSurrogate(input[i]))
            {
                return false; // Unexpected low surrogate
            }
        }

        return true;
    }

    private string ProcessUrlsAndEmails(string text)
    {
        // Replace URLs with placeholder text
        text = UrlRegex.Replace(text, match => " URL ");

        // Replace email addresses with readable text
        text = EmailRegex.Replace(
            text,
            match =>
            {
                var parts = match.Value.Split('@');
                if (parts.Length != 2)
                {
                    return " email address ";
                }

                var username = string.Join(" ", SplitCamelCase(parts[0]));
                var domain = parts[1].Replace(".", " dot ");

                return $" {username} at {domain} ";
            }
        );

        return text;
    }

    private IEnumerable<string> SplitCamelCase(string text)
    {
        var buffer = new StringBuilder();

        foreach (var c in text)
        {
            if (char.IsUpper(c) && buffer.Length > 0)
            {
                yield return buffer.ToString();
                buffer.Clear();
            }

            buffer.Append(char.ToLower(c));
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString();
        }
    }

    private string ExpandAbbreviations(string text)
    {
        foreach (var kvp in CommonAbbreviations)
        {
            text = kvp.Key.Replace(text, kvp.Value);
        }

        return text;
    }

    private string NormalizeWhitespaceAndPunctuation(string text)
    {
        // Replace non-standard whitespace with normal spaces
        text = NonStandardWhitespaceRegex.Replace(text, " ");

        // Collapse multiple spaces into one
        text = MultipleSpacesRegex.Replace(text, " ");

        // Fix spacing around punctuation
        text = SpaceBeforePunctuationRegex.Replace(text, "$1");

        return text;
    }
}
