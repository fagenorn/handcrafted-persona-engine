using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

public interface IConversationSession : IAsyncDisposable
{
    IConversationContext Context { get; }

    ConversationState CurrentState { get; }

    Guid SessionId { get; }

    /// <summary><see langword="true" /> when the session is in <see cref="ConversationState.Paused" />.</summary>
    bool IsPaused { get; }

    ValueTask RunAsync(CancellationToken cancellationToken);

    ValueTask StopAsync();

    /// <summary>
    ///     Transitions the session to <see cref="ConversationState.Paused" /> via the state
    ///     machine. The existing pause logic stops input adapters until <see cref="ResumeAsync" />
    ///     is called. No-op if already paused or not in a pauseable state.
    /// </summary>
    ValueTask PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Resumes a paused session by firing <see cref="ConversationTrigger.ResumeRequested" />.
    ///     No-op if not paused.
    /// </summary>
    ValueTask ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Raised whenever the session's FSM transitions to a new state.
    ///     Subscribers must be resilient to re-entrancy and thread-pool callbacks.
    /// </summary>
    event Action<ConversationState>? StateChanged;
}
