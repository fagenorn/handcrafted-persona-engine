namespace PersonaEngine.Lib.TTS.RVC;

public interface IRVCVoiceProvider : IAsyncDisposable
{
    /// <summary>
    ///     Gets the fullpath to the voice model
    /// </summary>
    /// <param name="voiceId">Voice identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Voice model path</returns>
    Task<string> GetVoiceAsync(string voiceId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets all available voice IDs
    /// </summary>
    /// <returns>List of voice IDs</returns>
    Task<IReadOnlyList<string>> GetAvailableVoicesAsync(
        CancellationToken cancellationToken = default
    );
}
