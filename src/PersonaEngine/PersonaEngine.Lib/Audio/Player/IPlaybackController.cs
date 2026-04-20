namespace PersonaEngine.Lib.Audio.Player;

/// <summary>
///     Interface for controlling audio playback scheduling and timing.
/// </summary>
public interface IPlaybackController : IDisposable
{
    /// <summary>
    ///     Gets the current playback time in seconds.
    /// </summary>
    float CurrentTime { get; }

    /// <summary>
    ///     Gets the total number of samples that have been scheduled for playback.
    /// </summary>
    long TotalSamplesPlayed { get; }

    /// <summary>
    ///     Gets the current latency in milliseconds.
    /// </summary>
    double CurrentLatencyMs { get; }

    /// <summary>
    ///     Gets whether latency information is available.
    /// </summary>
    bool HasLatencyInformation { get; }

    /// <summary>
    ///     Event raised when playback time changes significantly.
    /// </summary>
    event EventHandler<float> TimeChanged;

    /// <summary>
    ///     Event raised when latency changes significantly.
    /// </summary>
    event EventHandler<double> LatencyChanged;

    /// <summary>
    ///     Resets the controller to its initial state.
    /// </summary>
    void Reset();

    /// <summary>
    ///     Updates the latency information.
    /// </summary>
    /// <param name="latencyMs">The latency in milliseconds.</param>
    void UpdateLatency(double latencyMs);

    /// <summary>
    ///     Schedules a packet of audio data for playback.
    /// </summary>
    /// <param name="samplesPerChannel">The number of samples per channel in the packet.</param>
    /// <param name="sampleRate">The sample rate of the audio data.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    ///     A task that completes when the packet is scheduled, with a boolean indicating whether the packet should be
    ///     sent.
    /// </returns>
    Task<bool> SchedulePlaybackAsync(
        int samplesPerChannel,
        int sampleRate,
        CancellationToken cancellationToken
    );
}
