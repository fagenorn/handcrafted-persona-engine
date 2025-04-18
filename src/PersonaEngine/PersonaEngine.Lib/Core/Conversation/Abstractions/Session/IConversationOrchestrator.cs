namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

public interface IConversationOrchestrator : IAsyncDisposable
{
    IConversationSession GetSession(Guid sessionId);
    
    Task<Guid> StartNewSessionAsync(CancellationToken cancellationToken = default);

    ValueTask StopSessionAsync(Guid sessionId);

    IEnumerable<Guid> GetActiveSessionIds();

    ValueTask StopAllSessionsAsync();
    
    event EventHandler? SessionsUpdated;
}