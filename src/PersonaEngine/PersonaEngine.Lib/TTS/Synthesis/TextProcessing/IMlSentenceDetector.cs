namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Interface for ML-based sentence detection
/// </summary>
public interface IMlSentenceDetector : IDisposable
{
    /// <summary>
    ///     Detects sentences in text
    /// </summary>
    IReadOnlyList<string> Detect(string text);
}
