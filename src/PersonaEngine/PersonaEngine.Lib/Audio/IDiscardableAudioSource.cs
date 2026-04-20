namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Represents a source that can discard frames from the beginning of the audio stream if they are no longer needed.
/// </summary>
public interface IDiscardableAudioSource : IAudioSource
{
    /// <summary>
    ///     Discards a specified number of frames.
    /// </summary>
    /// <param name="count">The number of frames to discard.</param>
    void DiscardFrames(int count);
}
