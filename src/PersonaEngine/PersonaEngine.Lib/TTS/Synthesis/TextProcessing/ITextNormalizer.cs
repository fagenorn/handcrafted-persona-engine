namespace PersonaEngine.Lib.TTS.Synthesis.TextProcessing;

/// <summary>
///     Interface for text normalization
/// </summary>
public interface ITextNormalizer
{
    /// <summary>
    ///     Normalizes text for TTS synthesis
    /// </summary>
    /// <param name="text">Input text</param>
    /// <returns>Normalized text</returns>
    string Normalize(string text);
}
