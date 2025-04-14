namespace PersonaEngine.Lib.Core.Conversation.Contracts.Events;

/// <summary>
///     Marker event indicating the assistant has finished generating audible output.
/// </summary>
/// <param name="Timestamp">Timestamp when speaking stopped.</param>
/// <param name="Reason">Reason for stopping.</param>
/// <param name="RequestId">The ID of the request associated with this speech.</param>
public record AssistantSpeakingStopped(
    DateTimeOffset      Timestamp,
    AssistantStopReason Reason,
    Guid                RequestId 
);

/// <summary>
///     Reason for assistant stopping speech output.
/// </summary>
public enum AssistantStopReason
{
    Unknown,

    CompletedNaturally,

    Cancelled,

    Error
}