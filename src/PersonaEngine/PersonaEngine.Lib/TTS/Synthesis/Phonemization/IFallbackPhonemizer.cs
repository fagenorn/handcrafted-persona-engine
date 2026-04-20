namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Interface for fallback phoneme generation
/// </summary>
public interface IFallbackPhonemizer : IAsyncDisposable
{
    /// <summary>
    ///     Gets phonemes for a word when the lexicon fails
    /// </summary>
    Task<(string? Phonemes, int? Rating)> GetPhonemesAsync(
        string word,
        CancellationToken cancellationToken = default
    );
}
