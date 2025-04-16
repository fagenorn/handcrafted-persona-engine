namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Events;

public interface IConversationEvent
{
    Guid SessionId { get; }

    Guid? TurnId { get; }

    DateTimeOffset Timestamp { get; }
}