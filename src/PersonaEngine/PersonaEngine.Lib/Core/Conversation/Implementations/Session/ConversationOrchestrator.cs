using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.Core.Conversation.Implementations.Context;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Session;

public class ConversationOrchestrator(
    ILogger<ConversationOrchestrator> logger,
    IConversationSessionFactory sessionFactory,
    IOptions<ConversationOptions> conversationOptions,
    IOptionsMonitor<ConversationContextOptions> conversationContextOptions
) : IConversationOrchestrator
{
    private readonly ConcurrentDictionary<
        Guid,
        (IConversationSession Session, ValueTask RunTask)
    > _activeSessions = new();

    private readonly IOptionsMonitor<ConversationContextOptions> _conversationContextOptions =
        conversationContextOptions;

    private readonly IOptions<ConversationOptions> _conversationOptions = conversationOptions;

    private readonly ILogger<ConversationOrchestrator> _logger = logger;

    private readonly CancellationTokenSource _orchestratorCts = new();

    private readonly IConversationSessionFactory _sessionFactory = sessionFactory;

    public async ValueTask DisposeAsync()
    {
        await StopAllSessionsAsync();

        _orchestratorCts.Dispose();

        var remainingIds = _activeSessions.Keys.ToList();
        if (remainingIds.Count > 0)
        {
            _logger.LogWarning(
                "Found {Count} sessions remaining after StopAll. Force cleaning up.",
                remainingIds.Count
            );
            foreach (var id in remainingIds)
            {
                if (!_activeSessions.TryRemove(id, out var sessionInfo))
                {
                    continue;
                }

                try
                {
                    await sessionInfo.Session.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error during final forced disposal of session {SessionId}.",
                        id
                    );
                }
            }
        }

        _activeSessions.Clear();

        GC.SuppressFinalize(this);
    }

    public IConversationSession GetSession(Guid sessionId)
    {
        if (_activeSessions.TryGetValue(sessionId, out var sessionInfo))
        {
            return sessionInfo.Session;
        }

        throw new KeyNotFoundException($"Session {sessionId} not found in active sessions.");
    }

    public async Task<Guid> StartNewSessionAsync(CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid();
        var context = new ConversationContext([], _conversationContextOptions);
        var session = _sessionFactory.CreateSession(context, _conversationOptions.Value, sessionId);

        // Subscribe BEFORE RunAsync so the initial Initializing → Idle transition on fast-init
        // paths isn't missed. HandleSessionCompletionAsync.finally pairs this with -=.
        session.StateChanged += OnAnySessionStateChanged;

        try
        {
            var runTask = session.RunAsync(_orchestratorCts.Token);

            if (_activeSessions.TryAdd(session.SessionId, (null!, ValueTask.CompletedTask)))
            {
                _logger.LogInformation("Session {SessionId} started.", session.SessionId);

                var sessionJob = HandleSessionCompletionAsync(session, runTask);
                _activeSessions[session.SessionId] = (session, sessionJob);

                OnSessionsUpdated();

                return session.SessionId;
            }

            session.StateChanged -= OnAnySessionStateChanged;
            await session.StopAsync();
            await session.DisposeAsync();

            throw new InvalidOperationException(
                $"Failed to add session {session.SessionId} to the active pool."
            );
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Starting session {SessionId} was cancelled before RunAsync could start.",
                session.SessionId
            );
            session.StateChanged -= OnAnySessionStateChanged;
            await session.DisposeAsync();

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create or start session {SessionId}.",
                session.SessionId
            );
            session.StateChanged -= OnAnySessionStateChanged;
            await session.DisposeAsync();

            throw;
        }
    }

    public async ValueTask StopSessionAsync(Guid sessionId)
    {
        if (_activeSessions.TryGetValue(sessionId, out var sessionInfo))
        {
            try
            {
                await sessionInfo.Session.StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting stop for session {SessionId}.", sessionId);
            }
        }
        else
        {
            _logger.LogWarning(
                "Session {SessionId} not found in active sessions for stopping.",
                sessionId
            );
        }
    }

    public IEnumerable<Guid> GetActiveSessionIds()
    {
        return _activeSessions.Keys.ToList();
    }

    public int ActiveSessionCount => _activeSessions.Count;

    public bool TryGetFirstActiveSession(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IConversationSession? session
    )
    {
        // ConcurrentDictionary's struct enumerator is allocation-free and snapshots
        // a valid point-in-time view (items are never torn). We take the first
        // entry whose Session field has been populated — StartNewSessionAsync
        // briefly inserts a placeholder (null!, CompletedTask) before swapping in
        // the real tuple, so we must guard against the null sentinel here.
        foreach (var kvp in _activeSessions)
        {
            var candidate = kvp.Value.Session;
            if (candidate is not null)
            {
                session = candidate;
                return true;
            }
        }

        session = null;
        return false;
    }

    public async ValueTask StopAllSessionsAsync()
    {
        if (!_orchestratorCts.IsCancellationRequested)
        {
            await _orchestratorCts.CancelAsync();
        }

        var activeSessionIds = _activeSessions.Keys.ToList(); // Get a snapshot of IDs

        foreach (var sessionId in activeSessionIds)
        {
            if (!_activeSessions.TryGetValue(sessionId, out var sessionInfo))
            {
                continue;
            }

            try
            {
                await StopSessionAsync(sessionId);
                await sessionInfo.RunTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Exception occurred while waiting for sessions to stop during StopAllSessionsAsync."
                );
            }
        }
    }

    public ValueTask PauseAllSessionsAsync(CancellationToken cancellationToken = default) =>
        ForEachSessionAsync(
            (s, ct) => s.PauseAsync(ct),
            "Error pausing session {SessionId}.",
            cancellationToken
        );

    public ValueTask ResumeAllSessionsAsync(CancellationToken cancellationToken = default) =>
        ForEachSessionAsync(
            (s, ct) => s.ResumeAsync(ct),
            "Error resuming session {SessionId}.",
            cancellationToken
        );

    public ValueTask CancelActiveTurnsAsync(CancellationToken cancellationToken = default) =>
        ForEachSessionAsync(
            (s, ct) => s.CancelAsync(ct),
            "Error cancelling active turn on session {SessionId}.",
            cancellationToken
        );

    public ValueTask RetryErroredSessionsAsync(CancellationToken cancellationToken = default) =>
        ForEachSessionAsync(
            (s, ct) => s.RetryAsync(ct),
            "Error retrying session {SessionId}.",
            cancellationToken
        );

    /// <summary>
    ///     Fan out an independent per-session async operation across every active session and
    ///     await all of them concurrently. Each session's failure is logged with
    ///     <paramref name="errorMessageTemplate"/> (which must contain the <c>{SessionId}</c>
    ///     placeholder) and does not prevent other sessions from completing.
    /// </summary>
    private async ValueTask ForEachSessionAsync(
        Func<IConversationSession, CancellationToken, ValueTask> action,
        string errorMessageTemplate,
        CancellationToken cancellationToken
    )
    {
        var snapshot = _activeSessions.ToArray();
        if (snapshot.Length == 0)
        {
            return;
        }

        var tasks = new Task[snapshot.Length];
        for (var i = 0; i < snapshot.Length; i++)
        {
            var sessionId = snapshot[i].Key;
            var session = snapshot[i].Value.Session;
            tasks[i] = InvokeAsync(
                session,
                sessionId,
                action,
                errorMessageTemplate,
                cancellationToken
            );
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task InvokeAsync(
        IConversationSession? session,
        Guid sessionId,
        Func<IConversationSession, CancellationToken, ValueTask> action,
        string errorMessageTemplate,
        CancellationToken cancellationToken
    )
    {
        // Guard against the placeholder slot inserted by StartNewSessionAsync before
        // the real session tuple is swapped in.
        if (session is null)
        {
            return;
        }

        try
        {
            await action(session, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, errorMessageTemplate, sessionId);
        }
    }

    public event EventHandler? SessionsUpdated;

    /// <inheritdoc />
    public event Action<ConversationState>? StateChanged;

    private void OnAnySessionStateChanged(ConversationState s) => StateChanged?.Invoke(s);

    private async ValueTask HandleSessionCompletionAsync(
        IConversationSession session,
        ValueTask runTask
    )
    {
        try
        {
            await runTask;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Session {SessionId} RunAsync task was cancelled.",
                session.SessionId
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Session {SessionId} RunAsync task completed with an unhandled exception.",
                session.SessionId
            );
        }
        finally
        {
            if (_activeSessions.TryRemove(session.SessionId, out _))
            {
                _logger.LogInformation(
                    "Session {SessionId} removed from active sessions.",
                    session.SessionId
                );
                OnSessionsUpdated();
            }
            else
            {
                _logger.LogWarning(
                    "Session {SessionId} was already removed or not found during cleanup.",
                    session.SessionId
                );
            }

            session.StateChanged -= OnAnySessionStateChanged;

            try
            {
                await session.DisposeAsync();
                _logger.LogDebug("Session {SessionId} disposed successfully.", session.SessionId);
            }
            catch (Exception disposeEx)
            {
                _logger.LogError(
                    disposeEx,
                    "Error disposing session {SessionId}.",
                    session.SessionId
                );
            }
        }
    }

    protected virtual void OnSessionsUpdated()
    {
        SessionsUpdated?.Invoke(this, EventArgs.Empty);
    }
}
