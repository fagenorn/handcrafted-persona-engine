namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

public interface IConversationOrchestrator : IAsyncDisposable
{
    IConversationSession GetSession(Guid sessionId);

    Task<Guid> StartNewSessionAsync(CancellationToken cancellationToken = default);

    ValueTask StopSessionAsync(Guid sessionId);

    IEnumerable<Guid> GetActiveSessionIds();

    ValueTask StopAllSessionsAsync();

    /// <summary>
    ///     Pauses every active session by invoking <see cref="IConversationSession.PauseAsync" />.
    ///     Errors on individual sessions are logged and do not prevent other sessions from
    ///     being paused.
    /// </summary>
    ValueTask PauseAllSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Resumes every paused session by invoking <see cref="IConversationSession.ResumeAsync" />.
    ///     Errors on individual sessions are logged and do not prevent other sessions from
    ///     being resumed.
    /// </summary>
    ValueTask ResumeAllSessionsAsync(CancellationToken cancellationToken = default);

    event EventHandler? SessionsUpdated;

    /// <summary>
    ///     Aggregated state-change feed. Forwards transitions from every active session.
    ///     Consumers that care about a specific session should subscribe to it directly.
    /// </summary>
    event Action<ConversationState>? StateChanged;
}
