namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Handler raised for each buffer of mic samples. The <paramref name="samples" /> span is
///     only valid for the duration of the invocation — handlers must copy if they need to
///     retain the data.
/// </summary>
public delegate void AudioSamplesHandler(ReadOnlySpan<float> samples, int sampleRate);

/// <summary>
///     Interface for a microphone audio source.
///     Extends IAwaitableAudioSource with recording control methods
///     and device information retrieval.
/// </summary>
public interface IMicrophone : IAwaitableAudioSource
{
    /// <summary>Starts capturing audio from the microphone.</summary>
    void StartRecording();

    /// <summary>Stops capturing audio from the microphone.</summary>
    void StopRecording();

    /// <summary>Gets a list of available audio input device names.</summary>
    IEnumerable<string> GetAvailableDevices();

    /// <summary>
    ///     Raised each time a buffer of mic samples arrives from the underlying capture
    ///     backend. Listeners see the samples in the same call chain as the capture event
    ///     (no marshalling, no extra buffering).
    /// </summary>
    event AudioSamplesHandler? SamplesAvailable;
}
