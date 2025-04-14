namespace PersonaEngine.Lib.Core.Conversation.Contracts.Interfaces;

/// <summary>
///     Central coordinator for the conversation flow and state management.
/// </summary>
public interface IConversationOrchestrator : IAsyncDisposable
{
    /// <summary>
    ///     Starts the orchestrator's event processing loop.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Stops the orchestrator gracefully.
    /// </summary>
    Task StopAsync();

    // Maybe methods for external control if needed later?
    // Task PauseAsync();
    // Task ResumeAsync();
    // Task CancelCurrentActivityAsync();
}