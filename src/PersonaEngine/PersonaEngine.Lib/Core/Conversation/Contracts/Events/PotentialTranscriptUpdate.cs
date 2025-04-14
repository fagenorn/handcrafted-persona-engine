namespace PersonaEngine.Lib.Core.Conversation.Contracts.Events;

/// <summary>
///     Event published when a potential, non-final transcript segment is recognized.
/// </summary>
/// <param name="PartialText">The partial text recognized so far.</param>
/// <param name="SourceId">Identifier for the audio source (e.g., "Microphone1", "UserDiscordId").</param>
/// <param name="Timestamp">Timestamp when the event was generated.</param>
public record PotentialTranscriptUpdate(
    string         PartialText,
    string         SourceId,
    DateTimeOffset Timestamp
) : ITranscriptionEvent;