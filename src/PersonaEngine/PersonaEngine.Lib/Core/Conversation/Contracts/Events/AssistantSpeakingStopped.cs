namespace PersonaEngine.Lib.Core.Conversation.Contracts.Events;

/// <summary>
///     Marker event indicating the assistant has finished generating audible output.
/// </summary>
/// <param name="Timestamp">Timestamp when speaking stopped.</param>
/// <param name="Reason">Reason for stopping.</param>
public record AssistantSpeakingStopped(
    DateTimeOffset      Timestamp,
    AssistantStopReason Reason
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