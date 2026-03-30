namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Interface for sentence segmentation
/// </summary>
public interface ISentenceSegmenter
{
    /// <summary>
    ///     Splits text into sentences
    /// </summary>
    /// <param name="text">Input text</param>
    /// <returns>List of sentences</returns>
    IReadOnlyList<string> Segment(string text);
}
