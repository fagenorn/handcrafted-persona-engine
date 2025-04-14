namespace PersonaEngine.Lib.Core.Conversation.Contracts.Events;

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