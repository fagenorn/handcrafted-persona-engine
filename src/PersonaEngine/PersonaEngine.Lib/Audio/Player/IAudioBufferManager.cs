using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.Audio.Player;

/// <summary>
///     Manages audio buffers for playback.
/// </summary>
public interface IAudioBufferManager : IAsyncDisposable
{
    /// <summary>
    ///     Gets the number of audio buffers in the queue.
    /// </summary>
    int BufferCount { get; }

    bool ProducerCompleted { get; }

    /// <summary>
    ///     Enqueues audio segments for playback.
    /// </summary>
    /// <param name="audioSegments">Audio segments to enqueue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnqueueSegmentsAsync(
        IAsyncEnumerable<AudioSegment> audioSegments,
        CancellationToken cancellationToken
    );

    /// <summary>
    ///     Tries to get the next audio buffer from the queue.
    /// </summary>
    /// <param name="buffer">The audio buffer if available.</param>
    /// <param name="timeoutMs">Timeout in milliseconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a buffer was retrieved, false otherwise.</returns>
    bool TryGetNextBuffer(
        out (Memory<float> Data, AudioSegment Segment) buffer,
        int timeoutMs,
        CancellationToken cancellationToken
    );

    /// <summary>
    ///     Clears all buffers from the queue.
    /// </summary>
    void Clear();
}
