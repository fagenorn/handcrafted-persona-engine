using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

public interface IConversationSessionFactory
{
    IConversationSession CreateSession(ConversationOptions? options = null, Guid? sessionId = null);
}