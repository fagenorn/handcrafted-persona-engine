using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Interface for audio synthesis from phonemes
/// </summary>
public interface IAudioSynthesizer : IAsyncDisposable
{
    /// <summary>
    ///     Synthesizes audio from phonemes
    /// </summary>
    /// <param name="phonemes">Phoneme string</param>
    /// <param name="voiceId">Voice identifier</param>
    /// <param name="options">Synthesis options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Audio data with timing information</returns>
    Task<AudioData> SynthesizeAsync(
        string phonemes,
        KokoroVoiceOptions? options = null,
        CancellationToken cancellationToken = default
    );
}
