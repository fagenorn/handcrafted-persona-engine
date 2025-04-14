namespace PersonaEngine.Lib.Core.Conversation.Contracts.Interfaces;

/// <summary>
/// Handles text-to-speech synthesis and audio playback.
/// </summary>
public interface IAudioOutputService : IAsyncDisposable
{
    /// <summary>
    /// Synthesizes and plays the given text stream as audio.
    /// Publishes AssistantSpeakingStarted/Stopped events.
    /// </summary>
    /// <param name="textStream">The stream of text chunks to synthesize and play.</param>
    /// <param name="utteranceStartTime">Timestamp when the triggering user utterance started (for latency tracking).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when playback finishes, is cancelled, or errors.</returns>
    Task PlayTextStreamAsync(
        IAsyncEnumerable<string> textStream,
        DateTimeOffset?          utteranceStartTime,
        CancellationToken        cancellationToken);

    /// <summary>
    /// Stops any currently playing audio immediately.
    /// </summary>
    /// <returns>A task that completes when stopping is done.</returns>
    Task StopPlaybackAsync();
}