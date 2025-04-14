namespace PersonaEngine.Lib.Core.Conversation.Contracts.Events;

/// <summary>
/// Event published when user speech is detected while the assistant is speaking,
/// meeting barge-in criteria.
/// </summary>
/// <param name="SourceId">Identifier for the interrupting audio source.</param>
/// <param name="Timestamp">Timestamp when barge-in was detected.</param>
/// <param name="DetectedText">Optional: The text snippet that triggered the detection.</param>
public record BargeInDetected(
    string         SourceId,
    DateTimeOffset Timestamp,
    string?        DetectedText = null
);