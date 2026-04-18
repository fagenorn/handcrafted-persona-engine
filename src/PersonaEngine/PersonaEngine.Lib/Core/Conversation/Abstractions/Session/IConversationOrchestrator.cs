namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

public interface IConversationOrchestrator : IAsyncDisposable
{
    IConversationSession GetSession(Guid sessionId);

    Task<Guid> StartNewSessionAsync(CancellationToken cancellationToken = default);

    ValueTask StopSessionAsync(Guid sessionId);

    IEnumerable<Guid> GetActiveSessionIds();

    /// <summary>
    ///     Non-allocating single-session accessor for UI hot paths that only need
    ///     "the first session, if any". Returns <c>true</c> and sets
    ///     <paramref name="session"/> when at least one session is active; returns
    ///     <c>false</c> and <c>null</c> otherwise. Avoids the <see cref="List{T}"/>
    ///     allocation performed by <see cref="GetActiveSessionIds"/> plus the
    ///     subsequent <see cref="GetSession"/> lookup/throw cycle.
    /// </summary>
    bool TryGetFirstActiveSession([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IConversationSession? session);

    /// <summary>
    ///     Point-in-time active-session count. Allocation-free — unlike counting
    ///     <see cref="GetActiveSessionIds"/>, which materialises a list.
    /// </summary>
    int ActiveSessionCount { get; }

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

    /// <summary>
    ///     Invokes <see cref="IConversationSession.CancelAsync" /> on every active session.
    ///     Errors on individual sessions are logged and do not prevent other sessions from
    ///     being cancelled.
    /// </summary>
    ValueTask CancelActiveTurnsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Invokes <see cref="IConversationSession.RetryAsync" /> on every session currently
    ///     in <see cref="ConversationState.Error" />. Errors on individual sessions are
    ///     logged and do not prevent others from retrying.
    /// </summary>
    ValueTask RetryErroredSessionsAsync(CancellationToken cancellationToken = default);

    event EventHandler? SessionsUpdated;

    /// <summary>
    ///     Aggregated state-change feed. Forwards transitions from every active session.
    ///     Consumers that care about a specific session should subscribe to it directly.
    /// </summary>
    event Action<ConversationState>? StateChanged;
}
