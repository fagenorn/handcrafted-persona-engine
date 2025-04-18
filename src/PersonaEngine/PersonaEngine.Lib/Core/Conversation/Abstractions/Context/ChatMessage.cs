using OpenAI.Chat;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Context;

public class ChatMessage(Guid messageId, string participantId, string participantName, string text, DateTimeOffset timestamp, bool isPartial, ChatMessageRole role)
{
    public Guid MessageId { get; } = messageId;

    public string ParticipantId { get; } = participantId;

    public string ParticipantName { get; } = participantName;

    public string Text { get; internal set; } = text;

    public DateTimeOffset Timestamp { get; } = timestamp;

    public bool IsPartial { get; internal set; } = isPartial;

    public ChatMessageRole Role { get; } = role;
}