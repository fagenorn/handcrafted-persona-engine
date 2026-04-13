namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;

/// <summary>
/// Provides smoothed audio amplitude data from the current playback.
/// UI components read this instead of processing raw audio samples directly.
/// </summary>
public interface IAudioAmplitudeProvider
{
    /// <summary>
    /// Current smoothed amplitude in [0, 1] range.
    /// </summary>
    float CurrentAmplitude { get; }

    /// <summary>
    /// Whether audio is currently playing.
    /// </summary>
    bool IsPlaying { get; }
}
