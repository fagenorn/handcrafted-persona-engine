namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Context;

public sealed class TurnStartedEventArgs(Guid turnId, IReadOnlyList<string> participantIds) : EventArgs
{
    public Guid TurnId { get; } = turnId;

    public IReadOnlyList<string> ParticipantIds { get; } = participantIds;
}

public sealed class MessageAppendedEventArgs(
    Guid    turnId,
    string  participantId,
    string  delta,
    string  accumulatedText,
    string? emotion) : EventArgs
{
    public Guid TurnId { get; } = turnId;

    public string ParticipantId { get; } = participantId;

    /// <summary>Just the new tokens in this update.</summary>
    public string Delta { get; } = delta;

    /// <summary>Full accumulated text for this participant in the current turn.</summary>
    public string AccumulatedText { get; } = accumulatedText;

    /// <summary>
    /// Parsed emotion at the time of this delta, or null if none detected yet.
    /// May appear/change as more tokens arrive.
    /// </summary>
    public string? Emotion { get; } = emotion;
}

public sealed class TurnCompletedEventArgs(Guid turnId, IReadOnlyList<ChatMessage> messages, bool interrupted) : EventArgs
{
    public Guid TurnId { get; } = turnId;

    public IReadOnlyList<ChatMessage> Messages { get; } = messages;

    public bool Interrupted { get; } = interrupted;
}
