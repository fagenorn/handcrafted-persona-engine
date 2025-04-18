using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;
using PersonaEngine.Lib.Core.Conversation.Implementations.Context;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

public interface IConversationSessionFactory
{
    IConversationSession CreateSession(ConversationContext context, ConversationOptions? options = null, Guid? sessionId = null);
}