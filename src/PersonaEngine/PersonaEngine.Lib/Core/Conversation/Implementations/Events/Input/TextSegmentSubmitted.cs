using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Events.Input;

/// <summary>
/// A user-submitted text utterance that bypasses the speech pipeline.
/// Treated by the orchestrator as a completed finalized input, equivalent
/// in semantics to <see cref="SttSegmentRecognized"/> but without the
/// STT-specific fields (duration, confidence).
/// </summary>
public record TextSegmentSubmitted(
    Guid           SessionId,
    DateTimeOffset Timestamp,
    string         ParticipantId,
    string         Text
) : IInputEvent
{
    public Guid? TurnId { get; } = null;
}
