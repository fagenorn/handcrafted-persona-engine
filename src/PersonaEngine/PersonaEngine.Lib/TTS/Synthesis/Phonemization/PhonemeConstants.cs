namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Phonetic constants used in pronunciation
/// </summary>
public class PhonemeConstants
{
    public PhonemeConstants(bool useBritishEnglish)
    {
        UseBritishEnglish = useBritishEnglish;
    }

    /// <summary>
    ///     Whether to use British English pronunciation
    /// </summary>
    public bool UseBritishEnglish { get; init; }

    /// <summary>
    ///     US-specific tau sounds for flapping rules
    /// </summary>
    public HashSet<char> UsTauSounds { get; } = new("aeiouAEIOUæɑɒɔəɛɪʊʌᵻ");

    /// <summary>
    ///     Currency symbols with their word representations
    /// </summary>
    public Dictionary<char, (string Units, string Subunits)> CurrencyRepresentations { get; } =
        new()
        {
            { '$', ("dollar", "cent") },
            { '£', ("pound", "pence") },
            { '€', ("euro", "cent") },
        };

    public Dictionary<string, string> Symbols { get; } = new();
}
