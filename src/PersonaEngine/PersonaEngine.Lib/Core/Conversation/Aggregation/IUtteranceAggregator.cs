namespace PersonaEngine.Lib.Core.Conversation.Aggregation;

/// <summary>
/// Service responsible for aggregating transcript segments into complete utterances.
/// </summary>
public interface IUtteranceAggregator : IAsyncDisposable
{
    /// <summary>
    /// Starts processing transcription events to aggregate utterances.
    /// </summary>
    /// <param name="cancellationToken">Token to signal when to stop the service.</param>
    /// <returns>A task that completes when the service loop finishes.</returns>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops the aggregation process gracefully.
    /// </summary>
    /// <returns>A task that completes when stopping is finished.</returns>
    Task StopAsync();
}