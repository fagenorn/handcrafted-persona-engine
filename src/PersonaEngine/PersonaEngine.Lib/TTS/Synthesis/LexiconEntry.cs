using System.Text.Json.Serialization;

namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Lexicon entry representing pronunciation for a word
/// </summary>
public class LexiconEntry
{
    /// <summary>
    ///     Simple phoneme string (for entries with only one pronunciation)
    /// </summary>
    public string? Simple { get; set; }

    /// <summary>
    ///     Phoneme strings by part-of-speech tag
    /// </summary>
    public Dictionary<string, string>? ByTag { get; set; }

    /// <summary>
    ///     Whether this is a simple entry
    /// </summary>
    [JsonIgnore]
    public bool IsSimple => ByTag == null;

    /// <summary>
    ///     Gets phoneme string for a specific tag
    /// </summary>
    public string? GetForTag(string? tag)
    {
        if (IsSimple)
        {
            return Simple;
        }

        if (tag != null && ByTag != null && ByTag.TryGetValue(tag, out var value))
        {
            return value;
        }

        if (ByTag != null && ByTag.TryGetValue("DEFAULT", out var def))
        {
            return def;
        }

        return null;
    }

    /// <summary>
    ///     Whether this entry has a specific tag
    /// </summary>
    public bool HasTag(string tag)
    {
        return ByTag != null && ByTag.ContainsKey(tag);
    }
}
