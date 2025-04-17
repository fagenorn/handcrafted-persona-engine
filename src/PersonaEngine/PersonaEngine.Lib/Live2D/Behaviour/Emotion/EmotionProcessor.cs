using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.LLM;
using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.Live2D.Behaviour.Emotion;

/// <summary>
///     Processor for extracting emotion tags from text before TTS synthesis
/// </summary>
public class EmotionProcessor : ITextFilter
{
    // Regex to match emotion tags in the format [EMOTION:emoji]
    private static readonly Regex EmotionTagRegex = new(@"\[EMOTION:(.*?)\]",
                                                        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IEmotionService _emotionService;

    private readonly ILogger<EmotionProcessor> _logger;

    public EmotionProcessor(IEmotionService emotionService, ILoggerFactory loggerFactory)
    {
        _emotionService = emotionService ?? throw new ArgumentNullException(nameof(emotionService));
        _logger = loggerFactory?.CreateLogger<EmotionProcessor>() ??
                  throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <summary>
    ///     Priority of the emotion processor (should run before other text filters)
    /// </summary>
    public int Priority => 100;

    /// <summary>
    ///     Processes text to extract emotion tags and remove them from the text
    /// </summary>
    public ValueTask<TextFilterResult> ProcessAsync(string text, CancellationToken cancellationToken = default)
    {
        if ( string.IsNullOrEmpty(text) )
        {
            return ValueTask.FromResult(new TextFilterResult { ProcessedText = text });
        }

        cancellationToken.ThrowIfCancellationRequested();

        // List to store extracted emotions with their positions
        var emotions = new List<EmotionMarker>();

        // Extract emotion tags and their positions
        var lastIndex        = 0;
        var cleanTextBuilder = new StringBuilder();

        var matches = EmotionTagRegex.Matches(text);
        foreach ( Match match in matches )
        {
            // Add text before the emotion tag
            cleanTextBuilder.Append(text.Substring(lastIndex, match.Index - lastIndex));

            // Extract emotion emoji
            var emoji = match.Groups[1].Value;

            // Store position and emotion
            emotions.Add(new EmotionMarker { Position = cleanTextBuilder.Length, Emotion = emoji });

            _logger.LogDebug("Extracted emotion {Emoji} at position {Position}", emoji, cleanTextBuilder.Length);

            // Update last index
            lastIndex = match.Index + match.Length;
        }

        // Add remaining text
        cleanTextBuilder.Append(text.Substring(lastIndex));

        var processedText = cleanTextBuilder.ToString();
        _logger.LogDebug("Processed text from length {OriginalLength} to {ProcessedLength}",
                         text.Length, processedText.Length);

        // Create result
        var result = new TextFilterResult { ProcessedText = processedText, Metadata = new Dictionary<string, object> { ["Emotions"] = emotions } };

        return ValueTask.FromResult(result);
    }

    public ValueTask PostProcessAsync(TextFilterResult textFilterResult, AudioSegment segment, CancellationToken cancellationToken = default)
    {
        if ( textFilterResult.Metadata.TryGetValue("Emotions", out var emotionsObj) &&
             emotionsObj is List<EmotionMarker> emotions )
        {
            MapEmotionsToSegment(segment, emotions);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Maps emotions to audio segments based on token timing
    /// </summary>
    private void MapEmotionsToSegment(AudioSegment segment, List<EmotionMarker> emotions)
    {
        if ( emotions.Count == 0 || segment?.Tokens == null || !segment.Tokens.Any() )
        {
            return;
        }

        var emotionTimings = new List<EmotionTiming>();

        // Calculate character positions for each token
        var charPosition = 0;
        foreach ( var token in segment.Tokens )
        {
            // Skip tokens without timing information
            if ( !token.StartTs.HasValue || !token.EndTs.HasValue )
            {
                charPosition += token.Text.Length + token.Whitespace.Length;

                continue;
            }

            // Find emotions that fall within this token's text
            var tokenEnd      = charPosition + token.Text.Length;
            var tokenEmotions = emotions.Where(e => e.Position >= charPosition && e.Position < tokenEnd).ToList();

            foreach ( var emotion in tokenEmotions )
            {
                // Calculate relative position within the token
                var relativePos = token.Text.Length > 0
                                      ? (emotion.Position - charPosition) / (float)token.Text.Length
                                      : 0;

                // Interpolate the timestamp
                var timestamp = token.StartTs.Value + (token.EndTs.Value - token.StartTs.Value) * relativePos;

                emotionTimings.Add(new EmotionTiming { Timestamp = timestamp, Emotion = emotion.Emotion });

                _logger.LogDebug("Mapped emotion {Emoji} to timestamp {Timestamp:F2}s",
                                 emotion.Emotion, timestamp);
            }

            // Update character position
            charPosition = tokenEnd + token.Whitespace.Length;
        }

        // Register emotions with the service
        if ( emotionTimings.Any() )
        {
            _emotionService.RegisterEmotions(segment.Id, emotionTimings);
        }
    }
}