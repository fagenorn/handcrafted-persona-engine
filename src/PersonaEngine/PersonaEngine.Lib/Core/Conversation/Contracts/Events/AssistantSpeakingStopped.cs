namespace PersonaEngine.Lib.Core.Conversation.Contracts.Events;

/// <summary>
/// Marker event indicating the assistant has finished generating audible output.
/// </summary>
/// <param name="Timestamp">Timestamp when speaking stopped.</param>
/// <param name="Reason">Reason for stopping (e.g., CompletedNaturally, Cancelled, Error).</param>
public record AssistantSpeakingStopped(
    DateTimeOffset Timestamp,
    string         Reason = "Unknown"
);