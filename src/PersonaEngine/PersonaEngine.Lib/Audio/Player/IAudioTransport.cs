namespace PersonaEngine.Lib.Audio.Player;

/// <summary>
///     Defines methods for transporting audio data over a network.
/// </summary>
public interface IAudioTransport : IAsyncDisposable
{
    /// <summary>
    ///     Initializes the transport.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    ///     Sends an audio packet over the transport.
    /// </summary>
    /// <param name="audioData">Audio data to send.</param>
    /// <param name="sampleRate">Sample rate of the audio.</param>
    /// <param name="samplesPerChannel">Number of samples per channel.</param>
    /// <param name="channels">Number of audio channels.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendAudioPacketAsync(
        ReadOnlyMemory<float> audioData,
        int sampleRate,
        int samplesPerChannel,
        int channels,
        CancellationToken cancellationToken
    );

    Task FlushAsync(CancellationToken cancellationToken);
}
