namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

public interface IConversationOrchestrator : IAsyncDisposable
{
    Task<Guid> StartNewSessionAsync(CancellationToken cancellationToken = default);

    ValueTask StopSessionAsync(Guid sessionId);

    IEnumerable<Guid> GetActiveSessionIds();

    ValueTask StopAllSessionsAsync();
}