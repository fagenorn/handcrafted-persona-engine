using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Strategies;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Strategies;

public class NoSpeakingBargeInStrategy : IBargeInStrategy
{
    public bool ShouldAllowBargeIn(BargeInContext context) { return context.CurrentState != ConversationState.Speaking; }
}