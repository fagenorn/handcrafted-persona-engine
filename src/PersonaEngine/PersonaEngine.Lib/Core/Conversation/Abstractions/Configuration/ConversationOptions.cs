using PersonaEngine.Lib.Core.Conversation.Abstractions.Strategies;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;

public class ConversationOptions
{
    public BargeInStrategy BargeInBehavior { get; set; } = BargeInStrategy.InterruptAndAppend;

    public List<string>? InitialTopics { get; set; }

    public string? InitialContextDescription { get; set; }
}