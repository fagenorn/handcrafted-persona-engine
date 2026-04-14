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

    /// <summary>
    /// Emotion label parsed from the text (e.g. the value inside an
    /// <c>[EMOTION:...]</c> tag, or leading emoji fallback). Null when no
    /// emotion is detected. Populated by the context when the message is
    /// committed; consumers should not need to re-parse <see cref="Text"/>.
    /// </summary>
    public string? Emotion { get; internal set; }
}