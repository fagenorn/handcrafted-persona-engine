namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Result of phoneme conversion
/// </summary>
public class PhonemeResult
{
    /// <summary>
    ///     A placeholder empty result used when phonemization is not available or not required
    ///     (e.g., voice audition before Task 19 wires in the real phonemizer, or Qwen3 which ignores phonemes).
    /// </summary>
    public static readonly PhonemeResult Empty = new(string.Empty, []);

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
