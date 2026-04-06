namespace PersonaEngine.Lib.TTS.Synthesis.Kokoro;

public interface IKokoroVoiceProvider : IAsyncDisposable
{
    Task<VoiceData> GetVoiceAsync(string voiceId, CancellationToken ct);

    /// <summary>
    ///     Gets all available voice IDs
    /// </summary>
    /// <returns>List of voice IDs</returns>
    IReadOnlyList<string> GetAvailableVoices();
}
