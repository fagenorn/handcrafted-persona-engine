namespace PersonaEngine.Lib.Core.Conversation.Contracts.Events;

/// <summary>
///     Base interface for transcription-related events.
/// </summary>
public interface ITranscriptionEvent
{
    string SourceId { get; }

    DateTimeOffset Timestamp { get; }
}

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

/// <summary>
///     Event published when a user's utterance is considered complete,
///     typically after a period of silence.
/// </summary>
/// <param name="AggregatedText">The complete text of the utterance.</param>
/// <param name="SourceId">Identifier for the audio source.</param>
/// <param name="User">Identifier for the user associated with the source.</param>
/// <param name="StartTimestamp">Timestamp of the first segment in the utterance.</param>
/// <param name="EndTimestamp">Timestamp of the last segment in the utterance.</param>
/// <param name="Segments">Optional: The individual segments making up the utterance.</param>
public record UserUtteranceCompleted(
    string                                         AggregatedText,
    string                                         SourceId,
    string                                         User,
    DateTimeOffset                                 StartTimestamp,
    DateTimeOffset                                 EndTimestamp,
    IReadOnlyList<FinalTranscriptSegmentReceived>? Segments = null
);