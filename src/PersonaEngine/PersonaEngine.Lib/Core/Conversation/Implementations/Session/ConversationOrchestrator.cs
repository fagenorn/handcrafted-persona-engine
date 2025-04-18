using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.Core.Conversation.Implementations.Context;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Session;

public class ConversationOrchestrator : IConversationOrchestrator
{
    private readonly ConcurrentDictionary<Guid, (IConversationSession Session, ValueTask RunTask)> _activeSessions = new();

    private readonly IOptionsMonitor<ConversationContextOptions> _conversationContextOptions;

    private readonly IOptions<ConversationOptions> _conversationOptions;

    private readonly ILogger<ConversationOrchestrator> _logger;

    private readonly CancellationTokenSource _orchestratorCts = new();

    private readonly IConversationSessionFactory _sessionFactory;

    public ConversationOrchestrator(ILogger<ConversationOrchestrator> logger, IConversationSessionFactory sessionFactory, IOptions<ConversationOptions> conversationOptions, IOptionsMonitor<ConversationContextOptions> conversationContextOptions)
    {
        _logger                     = logger;
        _sessionFactory             = sessionFactory;
        _conversationOptions        = conversationOptions;
        _conversationContextOptions = conversationContextOptions;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAllSessionsAsync();

        _orchestratorCts.Dispose();

        var remainingIds = _activeSessions.Keys.ToList();
        if ( remainingIds.Count > 0 )
        {
            _logger.LogWarning("Found {Count} sessions remaining after StopAll. Force cleaning up.", remainingIds.Count);
            foreach ( var id in remainingIds )
            {
                if ( !_activeSessions.TryRemove(id, out var sessionInfo) )
                {
                    continue;
                }

                try
                {
                    await sessionInfo.Session.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during final forced disposal of session {SessionId}.", id);
                }
            }
        }

        _activeSessions.Clear();

        GC.SuppressFinalize(this);
    }

    public async Task<Guid> StartNewSessionAsync(CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid();
        var context   = new ConversationContext([], _conversationContextOptions);
        var session   = _sessionFactory.CreateSession(context, _conversationOptions.Value, sessionId);

        try
        {
            var runTask = session.RunAsync(_orchestratorCts.Token);

            if ( _activeSessions.TryAdd(session.SessionId, (null!, ValueTask.CompletedTask)) )
            {
                _logger.LogInformation("Session {SessionId} started.", session.SessionId);

                var sessionJob = HandleSessionCompletionAsync(session, runTask);
                _activeSessions[session.SessionId] = (session, sessionJob);

                return session.SessionId;
            }

            await session.StopAsync();
            await session.DisposeAsync();

            throw new InvalidOperationException($"Failed to add session {session.SessionId} to the active pool.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Starting session {SessionId} was cancelled before RunAsync could start.", session.SessionId);
            await session.DisposeAsync();

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create or start session {SessionId}.", session.SessionId);
            await session.DisposeAsync();

            throw;
        }
    }

    public async ValueTask StopSessionAsync(Guid sessionId)
    {
        if ( _activeSessions.TryGetValue(sessionId, out var sessionInfo) )
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
            _logger.LogWarning("Session {SessionId} not found in active sessions for stopping.", sessionId);
        }
    }

    public IEnumerable<Guid> GetActiveSessionIds() { return _activeSessions.Keys.ToList(); }

    public async ValueTask StopAllSessionsAsync()
    {
        if ( !_orchestratorCts.IsCancellationRequested )
        {
            await _orchestratorCts.CancelAsync();
        }

        var activeSessionIds = _activeSessions.Keys.ToList(); // Get a snapshot of IDs

        foreach ( var sessionId in activeSessionIds )
        {
            if ( !_activeSessions.TryGetValue(sessionId, out var sessionInfo) )
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
                _logger.LogError(ex, "Exception occurred while waiting for sessions to stop during StopAllSessionsAsync.");
            }
        }
    }

    private async ValueTask HandleSessionCompletionAsync(IConversationSession session, ValueTask runTask)
    {
        try
        {
            await runTask;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Session {SessionId} RunAsync task was cancelled.", session.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session {SessionId} RunAsync task completed with an unhandled exception.", session.SessionId);
        }
        finally
        {
            if ( _activeSessions.TryRemove(session.SessionId, out _) )
            {
                _logger.LogInformation("Session {SessionId} removed from active sessions.", session.SessionId);
            }
            else
            {
                _logger.LogWarning("Session {SessionId} was already removed or not found during cleanup.", session.SessionId);
            }

            try
            {
                await session.DisposeAsync();
                _logger.LogDebug("Session {SessionId} disposed successfully.", session.SessionId);
            }
            catch (Exception disposeEx)
            {
                _logger.LogError(disposeEx, "Error disposing session {SessionId}.", session.SessionId);
            }
        }
    }
}