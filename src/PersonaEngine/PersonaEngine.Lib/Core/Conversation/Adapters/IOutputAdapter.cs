namespace PersonaEngine.Lib.Core.Conversation.Adapters;

/// <summary>
///     Interface for components that send processed conversation output to external sinks.
///     Adapters typically subscribe to output request events from IChannelRegistry
///     and render the content to their specific target.
/// </summary>
public interface IOutputAdapter : IAsyncDisposable
{
    /// <summary>
    ///     Starts the adapter, preparing it to handle output requests (e.g., by starting
    ///     a background loop to read from an output channel).
    /// </summary>
    /// <param name="cancellationToken">Token to signal when to stop the service.</param>
    /// <returns>A task that completes when the adapter has started.</returns>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Stops the adapter gracefully.
    /// </summary>
    /// <returns>A task that completes when stopping is finished.</returns>
    Task StopAsync();
}