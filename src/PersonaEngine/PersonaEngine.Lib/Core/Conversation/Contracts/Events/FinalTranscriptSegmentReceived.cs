namespace PersonaEngine.Lib.Core.Conversation.Contracts.Events;

/// <summary>
///     Event published when a final transcript segment is recognized.
/// </summary>
/// <param name="Text">The final text of the segment.</param>
/// <param name="SourceId">Identifier for the audio source.</param>
/// <param name="User">Identifier for the user associated with the source (if available).</param>
/// <param name="Timestamp">Timestamp when the event was generated.</param>
public record FinalTranscriptSegmentReceived(
    string         Text,
    string         SourceId,
    string         User,
    DateTimeOffset Timestamp
) : ITranscriptionEvent;