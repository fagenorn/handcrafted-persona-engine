using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Context;

public interface IConversationCleanupStrategy
{
    bool Cleanup(List<InteractionTurn> history, ConversationContextOptions options, IReadOnlyDictionary<string, ParticipantInfo> participants);
}