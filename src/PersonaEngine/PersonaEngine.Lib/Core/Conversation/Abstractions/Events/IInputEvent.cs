namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Events;

public interface IInputEvent : IConversationEvent
{
    string ParticipantId { get; }
}