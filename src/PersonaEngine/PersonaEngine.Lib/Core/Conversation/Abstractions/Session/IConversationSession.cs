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
    ///     Raised when the session's FSM transitions to a new state. Invocations are
    ///     dispatched on the thread pool — subscribers may perform non-trivial work without
    ///     blocking the FSM callback, but must be thread-safe.
    /// </summary>
    event Action<ConversationState>? StateChanged;
}
