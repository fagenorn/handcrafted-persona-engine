namespace PersonaEngine.Lib.Core.Conversation.Adapters;

/// <summary>
///     Interface for components that bring external input into the conversation system.
///     Adapters are responsible for connecting to a source, processing raw data,
///     and publishing standardized events onto the appropriate IChannelRegistry channels.
/// </summary>
public interface IInputAdapter : IAsyncDisposable
{
    /// <summary>
    ///     Gets the unique identifier for this input source instance.
    /// </summary>
    string SourceId { get; }

    /// <summary>
    ///     Starts the adapter, connecting to the source and beginning event publication.
    /// </summary>
    /// <param name="cancellationToken">Token to signal when to stop the service.</param>
    /// <returns>A task that completes when the adapter has started.</returns>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Stops the adapter gracefully, disconnecting from the source.
    /// </summary>
    /// <returns>A task that completes when stopping is finished.</returns>
    Task StopAsync();
}