using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.LLM;
using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.Live2D.Behaviour.Emotion;

/// <summary>
///     Extracts [EMOTION:x] tags from text before TTS synthesis, records their
///     character offsets, and resolves them to audio timestamps during post-processing.
/// </summary>
public partial class EmotionProcessor(IEmotionService emotionService, ILoggerFactory loggerFactory)
    : ITextFilter
{
    private const string EmotionsKey = "Emotions";
    private const string CursorKey = "EmotionCursor";

    [GeneratedRegex(@"\[EMOTION:(.*?)\]")]
    private static partial Regex EmotionTagRegex();

    private readonly ILogger<EmotionProcessor> _logger =
        loggerFactory.CreateLogger<EmotionProcessor>();

    public int Priority => 100;

    /// <summary>
    ///     Lightweight record for emotion-to-character-offset mapping.
    /// </summary>
    internal readonly record struct EmotionCharMapping(int CharOffset, string Emotion);

    public ValueTask<TextFilterResult> ProcessAsync(
        string text,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(text))
        {
            return ValueTask.FromResult(new TextFilterResult { ProcessedText = text });
        }

        cancellationToken.ThrowIfCancellationRequested();

        var matches = EmotionTagRegex().Matches(text);
        if (matches.Count == 0)
        {
            return ValueTask.FromResult(new TextFilterResult { ProcessedText = text });
        }

        _logger.LogDebug("Found {Count} emotion tags.", matches.Count);

        var emotions = new List<EmotionCharMapping>(matches.Count);
        var cleanText = text;
        var offsetAdjustment = 0;

        foreach (Match match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var emotionValue = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(emotionValue))
            {
                _logger.LogWarning("Empty emotion tag at index {Index}, skipping.", match.Index);
                continue;
            }

            // Character offset in the clean text where this tag was removed
            var charOffset = match.Index - offsetAdjustment;
            emotions.Add(new EmotionCharMapping(charOffset, emotionValue));

            cleanText = cleanText.Remove(charOffset, match.Length);
            offsetAdjustment += match.Length;

            _logger.LogTrace(
                "Stripped emotion '{Emotion}' at char offset {Offset}.",
                emotionValue,
                charOffset
            );
        }

        var metadata = new Dictionary<string, object>();
        if (emotions.Count > 0)
        {
            metadata[EmotionsKey] = emotions;
            metadata[CursorKey] = 0;
        }

        return ValueTask.FromResult(
            new TextFilterResult { ProcessedText = cleanText, Metadata = metadata }
        );
    }

    public ValueTask PostProcessAsync(
        TextFilterResult textFilterResult,
        AudioSegment segment,
        CancellationToken cancellationToken = default
    )
    {
        if (
            segment.Tokens.Count == 0
            || !textFilterResult.Metadata.TryGetValue(EmotionsKey, out var emotionsObj)
            || emotionsObj is not List<EmotionCharMapping> emotions
            || emotions.Count == 0
        )
        {
            return ValueTask.CompletedTask;
        }

        if (!textFilterResult.Metadata.TryGetValue(CursorKey, out var cursorObj))
        {
            return ValueTask.CompletedTask;
        }

        var charCursor = (int)cursorObj;
        var emotionIdx = 0;

        // Skip already-processed emotions
        while (emotionIdx < emotions.Count && emotions[emotionIdx].CharOffset < charCursor)
        {
            emotionIdx++;
        }

        if (emotionIdx >= emotions.Count)
        {
            return ValueTask.CompletedTask;
        }

        var emotionTimings = new List<EmotionTiming>();

        foreach (var token in segment.Tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tokenEnd = charCursor + token.Text.Length + token.Whitespace.Length;

            // Register all emotions whose char offset falls within this token's range
            while (emotionIdx < emotions.Count && emotions[emotionIdx].CharOffset < tokenEnd)
            {
                var emotion = emotions[emotionIdx];
                emotionTimings.Add(
                    new EmotionTiming { Timestamp = token.StartTs ?? 0, Emotion = emotion.Emotion }
                );

                _logger.LogDebug(
                    "Mapped emotion '{Emotion}' at char {Offset} to timestamp {Ts:F2}s.",
                    emotion.Emotion,
                    emotion.CharOffset,
                    token.StartTs ?? 0
                );

                emotionIdx++;
            }

            charCursor = tokenEnd;
        }

        // Update cursor for next segment
        textFilterResult.Metadata[CursorKey] = charCursor;

        if (emotionTimings.Count > 0)
        {
            _logger.LogInformation(
                "Registering {Count} timed emotions for segment {SegmentId}.",
                emotionTimings.Count,
                segment.Id
            );

            try
            {
                emotionService.RegisterEmotions(segment.Id, emotionTimings);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error registering emotions for segment {SegmentId}.",
                    segment.Id
                );
            }
        }

        return ValueTask.CompletedTask;
    }
}
