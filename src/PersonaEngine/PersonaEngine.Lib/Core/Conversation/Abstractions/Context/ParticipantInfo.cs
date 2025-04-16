using OpenAI.Chat;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Context;

public record ParticipantInfo(
    string          Id,
    string          Name,
    ChatMessageRole Role
);