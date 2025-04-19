using PersonaEngine.Lib.Core.Conversation.Abstractions.Strategies;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Strategies;

public class IgnoreBargeInStrategy : IBargeInStrategy
{
    public bool ShouldAllowBargeIn(BargeInContext context) { return false; }
}