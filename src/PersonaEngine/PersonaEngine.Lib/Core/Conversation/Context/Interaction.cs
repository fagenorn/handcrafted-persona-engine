using OpenAI.Chat;

namespace PersonaEngine.Lib.Core.Conversation.Context;

/// <summary>
///     Represents a single turn or interaction within the conversation.
/// </summary>
/// <param name="SourceId">Identifier for the source of the interaction (e.g., user ID, "Assistant").</param>
/// <param name="Role">The role of the participant (e.g., User, Assistant).</param>
/// <param name="Content">The textual content of the interaction.</param>
/// <param name="Timestamp">When the interaction occurred.</param>
public record Interaction(
    string          SourceId,
    ChatMessageRole Role,
    string          Content,
    DateTimeOffset  Timestamp
);