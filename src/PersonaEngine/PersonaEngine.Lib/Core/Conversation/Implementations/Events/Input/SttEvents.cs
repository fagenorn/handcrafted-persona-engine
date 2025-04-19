using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Events.Input;

public record SttSegmentRecognizing(
    Guid           SessionId,
    DateTimeOffset Timestamp,
    string         ParticipantId,
    string         PartialTranscript,
    TimeSpan       Duration
) : IInputEvent
{
    public Guid? TurnId { get; } = null;
}

public record SttSegmentRecognized(
    Guid           SessionId,
    DateTimeOffset Timestamp,
    string         ParticipantId,
    string         FinalTranscript,
    TimeSpan       Duration,
    float?         Confidence
) : IInputEvent
{
    public Guid? TurnId { get; } = null;
}