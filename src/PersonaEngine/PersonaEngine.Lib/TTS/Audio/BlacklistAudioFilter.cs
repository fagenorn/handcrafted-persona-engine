using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PersonaEngine.Lib.TTS.Profanity;
using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.TTS.Audio;

/// <summary>
///     Audio filter that censors profane content by replacing it with a beep tone.
/// </summary>
public class BlacklistAudioFilter : IAudioFilter
{
    // Configuration constants
    private const float DEFAULT_BEEP_FREQUENCY = 880; // Changed from 400Hz to 1000Hz (standard censor beep)

    private const float DEFAULT_BEEP_VOLUME = 0.35f; // Slightly increased for better audibility

    private const int REFERENCE_SAMPLE_RATE = 24000;

    // Common suffixes to check when matching profanity
    private static readonly string[] COMMON_SUFFIXES = { "s", "es", "ed", "ing", "er", "ers" };

    // Pre-calculated beep tone samples
    private readonly float[] _beepCycle;

    private readonly float _beepFrequency;

    private readonly float _beepVolume;

    private readonly ProfanityDetector _profanityDetector;

    private readonly HashSet<string> _profanityDictionary;

    // Cache for previously checked words to improve performance
    private readonly Dictionary<string, bool> _wordCheckCache = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>
    ///     Initializes a new instance of the <see cref="BlacklistAudioFilter" /> class.
    /// </summary>
    /// <param name="profanityDetector">The profanity detector service.</param>
    public BlacklistAudioFilter(ProfanityDetector profanityDetector)
        : this(profanityDetector, DEFAULT_BEEP_FREQUENCY) { }

    /// <summary>
    ///     Initializes a new instance of the <see cref="BlacklistAudioFilter" /> class with custom audio parameters.
    /// </summary>
    /// <param name="profanityDetector">The profanity detector service.</param>
    /// <param name="beepFrequency">The frequency of the beep tone in Hz.</param>
    /// <param name="beepVolume">The volume of the beep tone (0.0-1.0).</param>
    /// <exception cref="ArgumentNullException">Thrown when profanityDetector is null.</exception>
    public BlacklistAudioFilter(
        ProfanityDetector profanityDetector,
        float beepFrequency = DEFAULT_BEEP_FREQUENCY,
        float beepVolume = DEFAULT_BEEP_VOLUME
    )
    {
        _profanityDetector =
            profanityDetector ?? throw new ArgumentNullException(nameof(profanityDetector));
        _beepFrequency = beepFrequency;
        _beepVolume = Math.Clamp(beepVolume, 0.0f, 1.0f);

        // Load profanity dictionary
        var profanityListPath = ModelUtils.GetModelPath(ModelType.BadWords);
        _profanityDictionary = LoadProfanityDictionary(profanityListPath);

        // Pre-calculate one cycle of the beep tone for efficient reuse
        // Using a square wave instead of sine wave for a more traditional censorship beep
        var samplesPerCycle = (int)(REFERENCE_SAMPLE_RATE / _beepFrequency);
        _beepCycle = new float[samplesPerCycle];

        for (var i = 0; i < samplesPerCycle; i++)
        {
            // Generate square wave (values are either +volume or -volume)
            _beepCycle[i] = i < samplesPerCycle / 2 ? _beepVolume : -_beepVolume;
        }
    }

    /// <summary>
    ///     Processes an audio segment to censor profane content with beep tones.
    /// </summary>
    /// <param name="segment">The audio segment to process.</param>
    public void Process(AudioSegment segment)
    {
        if (
            segment == null
            || segment.AudioData.IsEmpty
            || segment.Tokens == null
            || segment.Tokens.Count == 0
        )
        {
            return; // Early exit for invalid segments
        }

        var tokens = segment.Tokens;
        var audioSpan = segment.AudioData.Span;
        var sampleRate = segment.SampleRate;

        // First step: Identify and mark profane tokens
        MarkProfaneTokens(tokens);

        // Second step: Apply beep sound to marked tokens
        foreach (var (token, index) in tokens.Select((t, i) => (t, i)))
        {
            if (token is not { Text: "[REDACTED]" })
            {
                continue; // Skip null or non-redacted tokens
            }

            // Determine valid time boundaries for the audio replacement
            var (startTime, endTime) = DetermineTimeBoundaries(token, tokens, index);

            // Convert timestamps to sample indices
            var startIndex = (int)(startTime * sampleRate);
            var endIndex = (int)(endTime * sampleRate);

            // Validate indices to prevent out-of-bounds access
            startIndex = Math.Max(0, startIndex);
            endIndex = Math.Min(audioSpan.Length, endIndex);

            if (startIndex >= endIndex || startIndex >= audioSpan.Length)
            {
                continue; // Invalid range
            }

            // Apply beep tone to the segment
            ApplyBeepTone(audioSpan, startIndex, endIndex, sampleRate);
        }
    }

    public int Priority => -100;

    /// <summary>
    ///     Marks tokens containing profanity based on the overall severity level.
    /// </summary>
    /// <param name="tokens">The list of tokens to process.</param>
    /// <param name="tolerance">The tolerance level for profanity.</param>
    private void MarkProfaneTokens(
        IReadOnlyList<Token> tokens,
        ProfanitySeverity tolerance = ProfanitySeverity.Clean
    )
    {
        if (tokens.Count == 0)
        {
            return;
        }

        // Build the full sentence from tokens for overall evaluation
        var sentence = BuildSentenceFromTokens(tokens);

        // Evaluate overall profanity severity
        var overallSeverity = _profanityDetector.EvaluateProfanity(sentence);

        // If the overall severity is within tolerance, no need to censor
        if (overallSeverity <= tolerance)
        {
            return;
        }

        // Mark individual profane tokens
        foreach (var token in tokens)
        {
            if (token == null || string.IsNullOrWhiteSpace(token.Text))
            {
                continue;
            }

            // Split the token into words to check each individually
            var words = SplitIntoWords(token.Text);

            foreach (var word in words)
            {
                if (IsProfaneWord(word))
                {
                    token.Text = "[REDACTED]";

                    break; // Break once we find any profanity in the token
                }
            }
        }
    }

    /// <summary>
    ///     Splits text into individual words, preserving only alphanumeric characters.
    /// </summary>
    private static IEnumerable<string> SplitIntoWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        // Use regex to split text into words (sequences of letters)
        var matches = Regex.Matches(text, @"\b[a-zA-Z]+\b");

        foreach (Match match in matches)
        {
            yield return match.Value;
        }
    }

    /// <summary>
    ///     Builds a complete sentence from a list of tokens.
    /// </summary>
    private static string BuildSentenceFromTokens(IReadOnlyList<Token> tokens)
    {
        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        // Add all tokens except the last one with their whitespace
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            if (tokens[i] == null || tokens[i].Text == null)
            {
                continue;
            }

            builder.Append(tokens[i].Text);
            if (tokens[i].Whitespace == " ")
            {
                builder.Append(' ');
            }
        }

        // Add the last token
        if (tokens[^1] != null && tokens[^1].Text != null)
        {
            builder.Append(tokens[^1].Text);
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Determines the start and end time boundaries for audio replacement.
    /// </summary>
    private (double Start, double End) DetermineTimeBoundaries(
        Token token,
        IReadOnlyList<Token> allTokens,
        int tokenIndex
    )
    {
        if (token == null)
        {
            return (0, 0); // Default for null tokens
        }

        double startTime,
            endTime;

        // Handle the case where this is the first token with missing timestamps
        if (tokenIndex == 0 && (!token.StartTs.HasValue || !token.EndTs.HasValue))
        {
            startTime = 0;

            // For end time, use token's end time if available, otherwise use a reasonable default
            endTime = token.EndTs ?? EstimateTokenDuration(token);
        }
        else
        {
            // Determine start time
            if (!token.StartTs.HasValue)
            {
                // If no start time is available, use previous token's end time if possible
                if (tokenIndex > 0 && allTokens[tokenIndex - 1] != null)
                {
                    var prevToken = allTokens[tokenIndex - 1];
                    startTime = prevToken.EndTs ?? prevToken.StartTs ?? 0;
                }
                else
                {
                    startTime = 0;
                }
            }
            else
            {
                startTime = token.StartTs.Value;
            }

            // Determine end time
            if (!token.EndTs.HasValue)
            {
                // If no end time is available, check if we can use the next token's start time
                if (tokenIndex < allTokens.Count - 1 && allTokens[tokenIndex + 1] != null)
                {
                    var nextToken = allTokens[tokenIndex + 1];
                    endTime = nextToken.StartTs ?? startTime + EstimateTokenDuration(token);
                }
                else
                {
                    // For the last token, estimate a reasonable duration
                    endTime = startTime + EstimateTokenDuration(token);
                }
            }
            else
            {
                endTime = token.EndTs.Value;
            }
        }

        // Ensure we always have a positive duration
        if (endTime <= startTime)
        {
            endTime = startTime + 0.1; // Add minimal duration if times are invalid
        }

        return (startTime, endTime);
    }

    /// <summary>
    ///     Estimates a reasonable duration for a token based on its content length.
    /// </summary>
    private double EstimateTokenDuration(Token token)
    {
        if (token == null)
        {
            return 0.1; // Default for null tokens
        }

        // Average speaking rate is roughly 5-6 characters per second
        const double charsPerSecond = 5.5;

        // Minimum duration to ensure even single characters get enough time
        const double minimumDuration = 0.1;

        return Math.Max(minimumDuration, (token.Text?.Length ?? 0) / charsPerSecond);
    }

    /// <summary>
    ///     Applies a beep tone to a section of audio with smooth fade-in and fade-out.
    /// </summary>
    private void ApplyBeepTone(Span<float> audioData, int startIndex, int endIndex, int sampleRate)
    {
        var length = endIndex - startIndex;
        if (length <= 0)
        {
            return;
        }

        // Calculate how to map our pre-calculated beep cycle to the current sample rate
        var cycleScaleFactor = (double)sampleRate / REFERENCE_SAMPLE_RATE;

        // Number of samples for one complete cycle at the current sample rate
        var samplesPerCycle = (int)(_beepCycle.Length * cycleScaleFactor);

        // Make sure we have at least one sample per cycle
        samplesPerCycle = Math.Max(1, samplesPerCycle);

        // Apply a short fade-in and fade-out to avoid clicks
        // 5ms fade at current sample rate or 1/4 of segment length, whichever is smaller
        var fadeLength = Math.Min((int)(0.005 * sampleRate), length / 4);
        fadeLength = Math.Max(1, fadeLength); // Ensure at least 1 sample for fade

        for (var i = 0; i < length; i++)
        {
            // Calculate the position in the beep cycle
            var cycleIndex = (int)(i % samplesPerCycle / cycleScaleFactor) % _beepCycle.Length;

            // Get beep sample from pre-calculated cycle (with bounds check)
            var beepSample = _beepCycle[Math.Max(0, Math.Min(cycleIndex, _beepCycle.Length - 1))];

            // Apply fade-in and fade-out for smoother audio transitions
            var fadeMultiplier = 1.0f;
            if (i < fadeLength)
            {
                fadeMultiplier = (float)i / fadeLength; // Linear fade-in
            }
            else if (i > length - fadeLength)
            {
                fadeMultiplier = (float)(length - i) / fadeLength; // Linear fade-out
            }

            // Replace the original sample with the beep
            audioData[startIndex + i] = beepSample * fadeMultiplier;
        }
    }

    /// <summary>
    ///     Checks if the given word is profane, including checking for variations.
    /// </summary>
    private bool IsProfaneWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return false;
        }

        // Check cache first to avoid redundant processing
        if (_wordCheckCache.TryGetValue(word, out var result))
        {
            return result;
        }

        // Normalize the word
        var normalized = NormalizeText(word);

        // Direct match check
        if (_profanityDictionary.Contains(normalized))
        {
            _wordCheckCache[word] = true;

            return true;
        }

        // Check for variations with common suffixes
        foreach (var suffix in COMMON_SUFFIXES)
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                // Try removing the suffix and check if the base word is profane
                var baseWord = normalized.Substring(0, normalized.Length - suffix.Length);

                // Only check non-empty base words
                if (!string.IsNullOrEmpty(baseWord) && _profanityDictionary.Contains(baseWord))
                {
                    _wordCheckCache[word] = true;

                    return true;
                }
            }
        }

        // Word not found to be profane after all checks
        _wordCheckCache[word] = false;

        return false;
    }

    /// <summary>
    ///     Normalizes text by removing diacritics, punctuation, and converting to lowercase.
    /// </summary>
    private static string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Convert to lowercase and normalize diacritics
        var normalized = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);

        // Remove diacritical marks and punctuation in a single pass for efficiency
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            // Keep only characters that are not diacritics or punctuation
            if (
                CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark
                && !char.IsPunctuation(c)
            )
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    ///     Loads the profanity dictionary from a file with error handling.
    /// </summary>
    private static HashSet<string> LoadProfanityDictionary(string filePath)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Console.Error.WriteLine($"Warning: Profanity list file not found at: {filePath}");

                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return new HashSet<string>(
                File.ReadAllLines(filePath)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(word => NormalizeText(word.Trim())),
                StringComparer.OrdinalIgnoreCase
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error loading profanity dictionary: {ex.Message}");

            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
