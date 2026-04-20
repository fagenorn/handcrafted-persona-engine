namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Interface for converting text to phonemes
/// </summary>
public interface IPhonemizer : IDisposable
{
    /// <summary>
    ///     Converts text to phoneme representation
    /// </summary>
    /// <param name="text">Input text</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Phoneme result</returns>
    Task<PhonemeResult> ToPhonemesAsync(string text, CancellationToken cancellationToken = default);
}
