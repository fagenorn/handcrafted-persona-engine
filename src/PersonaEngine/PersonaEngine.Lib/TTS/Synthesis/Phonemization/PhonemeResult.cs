namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Result of phoneme conversion
/// </summary>
public class PhonemeResult
{
    public PhonemeResult(string phonemes, Token[] tokens)
    {
        Phonemes = phonemes;
        Tokens = tokens;
    }

    /// <summary>
    ///     Phoneme string representation
    /// </summary>
    public string Phonemes { get; }

    /// <summary>
    ///     Tokens with phonetic information
    /// </summary>
    public Token[] Tokens { get; }
}
