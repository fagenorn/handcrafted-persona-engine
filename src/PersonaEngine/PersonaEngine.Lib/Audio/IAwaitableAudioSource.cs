namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Represents an audio source that can be awaited for new samples and can be flushed.
/// </summary>
public interface IAwaitableAudioSource : IAudioSource
{
    bool IsFlushed { get; }

    /// <summary>
    ///     Waits for new samples to be available.
    /// </summary>
    /// <param name="sampleCount">The sample count to wait for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of times the wait was performed.</returns>
    Task WaitForInitializationAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Waits for the source to have at least the specified number of samples.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task WaitForNewSamplesAsync(long sampleCount, CancellationToken cancellationToken);

    /// <summary>
    ///     Waits for the source to be at least the specified duration.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    Task WaitForNewSamplesAsync(TimeSpan minimumDuration, CancellationToken cancellationToken);

    /// <summary>
    ///     Flushes the stream, indicating that no more data will be written.
    /// </summary>
    void Flush();
}
