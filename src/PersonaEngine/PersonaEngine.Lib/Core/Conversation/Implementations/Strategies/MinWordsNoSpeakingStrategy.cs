using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Strategies;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Strategies;

public class MinWordsNoSpeakingStrategy(MinWordsBargeInStrategy inner) : IBargeInStrategy
{
    public bool ShouldAllowBargeIn(BargeInContext context)
    {
        if (context.CurrentState == ConversationState.Speaking)
        {
            return false;
        }

        return inner.ShouldAllowBargeIn(context);
    }
}
