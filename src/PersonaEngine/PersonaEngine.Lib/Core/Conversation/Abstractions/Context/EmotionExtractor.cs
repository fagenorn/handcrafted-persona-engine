using System.Text.RegularExpressions;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Context;

/// <summary>
/// Extracts an emotion label from an assistant message.
/// Looks for an <c>[EMOTION:label]</c> tag anywhere in the text (last wins),
/// then falls back to a leading emoji character. Returns the cleaned text
/// (with [EMOTION:...] tags removed) and the extracted label (or null).
/// </summary>
public static class EmotionExtractor
{
    private static readonly Regex EmotionTagRegex = new(@"\[EMOTION:(.*?)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LeadingEmojiRegex = new(
        @"^\s*(\p{Cs}\p{Cs}|[\u2600-\u27BF])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static (string CleanText, string? Emotion) Extract(string text)
    {
        if ( string.IsNullOrEmpty(text) )
        {
            return (text, null);
        }

        var matches = EmotionTagRegex.Matches(text);
        if ( matches.Count > 0 )
        {
            var emotion = matches[^1].Groups[1].Value.Trim();
            var cleaned = EmotionTagRegex.Replace(text, "").Trim();
            return (cleaned, string.IsNullOrEmpty(emotion) ? null : emotion);
        }

        var emojiMatch = LeadingEmojiRegex.Match(text);
        if ( emojiMatch.Success )
        {
            return (text, emojiMatch.Groups[1].Value);
        }

        return (text, null);
    }
}
