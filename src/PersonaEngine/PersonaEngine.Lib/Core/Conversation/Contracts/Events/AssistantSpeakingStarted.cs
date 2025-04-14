namespace PersonaEngine.Lib.Core.Conversation.Contracts.Events;

/// <summary>
/// Marker event indicating the assistant has started generating audible output (TTS/Audio).
/// </summary>
/// <param name="Timestamp">Timestamp when speaking started.</param>
/// <param name="RequestId">The ID of the request associated with this speech.</param>
public record AssistantSpeakingStarted(
    DateTimeOffset Timestamp,
    Guid           RequestId
);