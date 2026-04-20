namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Interface for part-of-speech tagging
/// </summary>
public interface IPosTagger : IDisposable
{
    /// <summary>
    ///     Tags parts of speech in text
    /// </summary>
    Task<IReadOnlyList<PosToken>> TagAsync(
        string text,
        CancellationToken cancellationToken = default
    );
}
