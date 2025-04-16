using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

public interface IConversationSessionManager : IAsyncDisposable
{
    ValueTask<IConversationSession> CreateSessionAsync(ConversationOptions? options = null, Guid? sessionId = null);

    IConversationSession? GetSession(Guid sessionId);

    IEnumerable<IConversationSession> GetAllActiveSessions();

    ValueTask TerminateSessionAsync(Guid sessionId);

    ValueTask TerminateAllSessionsAsync();
}