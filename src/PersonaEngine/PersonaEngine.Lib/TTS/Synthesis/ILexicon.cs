namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Interface for phoneme lexicon lookup
/// </summary>
public interface ILexicon
{
    public (string? Phonemes, int? Rating) ProcessToken(Token token, TokenContext ctx);
}
