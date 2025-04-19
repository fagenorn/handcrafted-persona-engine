using PersonaEngine.Lib.Core.Conversation.Abstractions.Strategies;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;

public record ConversationOptions
{
    public BargeInType BargeInType { get; set; } = BargeInType.MinWords;

    public int BargeInMinWords { get; set; } = 2;
}