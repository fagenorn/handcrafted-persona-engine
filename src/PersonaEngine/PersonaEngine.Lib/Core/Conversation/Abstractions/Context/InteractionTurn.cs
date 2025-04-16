using OpenAI.Chat;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Context;

public record InteractionTurn(
    Guid                                    TurnId,
    IEnumerable<string>                     ParticipantIds,
    DateTimeOffset                          StartTime,
    DateTimeOffset?                         EndTime,
    Dictionary<string, LlmResponseDetails> ParticipantResponses
);

public record LlmResponseDetails(
    string           FinalText,
    bool             WasPartial,
    ChatMessageRole  Role
);