namespace PersonaEngine.Lib.TTS.RVC;

public interface IRVCVoiceProvider : IAsyncDisposable
{
    /// <summary>
    ///     Gets voice model info including path and output sample rate.
    /// </summary>
    /// <param name="voiceId">Voice identifier</param>
    /// <returns>Voice model metadata</returns>
    RvcVoiceInfo GetVoice(string voiceId);

    /// <summary>
    ///     Gets all available voice IDs
    /// </summary>
    /// <returns>List of voice IDs</returns>
    IReadOnlyList<string> GetAvailableVoices();
}
