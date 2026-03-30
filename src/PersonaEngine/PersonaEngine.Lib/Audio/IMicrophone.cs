namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Interface for a microphone audio source.
///     Extends IAwaitableAudioSource with recording control methods
///     and device information retrieval.
/// </summary>
public interface IMicrophone : IAwaitableAudioSource
{
    /// <summary>
    ///     Starts capturing audio from the microphone.
    /// </summary>
    void StartRecording();

    /// <summary>
    ///     Stops capturing audio from the microphone.
    /// </summary>
    void StopRecording();

    /// <summary>
    ///     Gets a list of available audio input device names.
    /// </summary>
    /// <returns>An enumerable collection of device names.</returns>
    IEnumerable<string> GetAvailableDevices();
}
