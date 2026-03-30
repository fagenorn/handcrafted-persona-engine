namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Result of text preprocessing
/// </summary>
public class ProcessedText
{
    public ProcessedText(string normalizedText, IReadOnlyList<string> sentences)
    {
        NormalizedText = normalizedText;
        Sentences = sentences;
    }

    /// <summary>
    ///     Normalized text
    /// </summary>
    public string NormalizedText { get; }

    /// <summary>
    ///     Segmented sentences
    /// </summary>
    public IReadOnlyList<string> Sentences { get; }
}
