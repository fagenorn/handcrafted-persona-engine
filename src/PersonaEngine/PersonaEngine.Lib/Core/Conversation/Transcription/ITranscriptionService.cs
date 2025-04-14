namespace PersonaEngine.Lib.Core.Conversation.Transcription;

/// <summary>
/// Service responsible for handling audio input and generating transcription events.
/// </summary>
public interface ITranscriptionService : IAsyncDisposable
{
    /// <summary>
    /// Starts the transcription process.
    /// </summary>
    /// <param name="cancellationToken">Token to signal when to stop the service.</param>
    /// <returns>A task that completes when the service loop finishes.</returns>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops the transcription process gracefully.
    /// </summary>
    /// <returns>A task that completes when stopping is finished.</returns>
    Task StopAsync();
}