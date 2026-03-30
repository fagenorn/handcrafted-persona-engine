namespace PersonaEngine.Lib.TTS.Synthesis;

public interface IKokoroVoiceProvider : IAsyncDisposable
{
    Task<VoiceData> GetVoiceAsync(string voiceId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets all available voice IDs
    /// </summary>
    /// <returns>List of voice IDs</returns>
    Task<IReadOnlyList<string>> GetAvailableVoicesAsync(
        CancellationToken cancellationToken = default
    );
}
