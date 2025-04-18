using PersonaEngine.Lib.Core.Conversation.Abstractions.Strategies;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;

public record ConversationOptions
{
    public BargeInStrategy BargeInBehavior { get; set; } = BargeInStrategy.InterruptAndAppend;
}