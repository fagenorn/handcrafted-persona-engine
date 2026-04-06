namespace PersonaEngine.Lib.TTS.Synthesis.TextProcessing;

/// <summary>
///     Interface for text preprocessing
/// </summary>
public interface ITextProcessor
{
    /// <summary>
    ///     Processes raw text for TTS synthesis
    /// </summary>
    /// <param name="text">Input text</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processed text with sentences</returns>
    Task<ProcessedText> ProcessAsync(string text, CancellationToken cancellationToken = default);
}
