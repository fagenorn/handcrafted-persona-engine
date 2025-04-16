using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Events.Input;

public record AudioPlaybackEndedEvent(Guid SessionId, Guid? TurnId, DateTimeOffset Timestamp, CompletionReason FinishReason) : IOutputEvent
{
    public string? ParticipantId => "Assistant";
}

public record AudioPlaybackStartedEvent(Guid SessionId, Guid? TurnId, DateTimeOffset Timestamp) : IOutputEvent
{
    public string? ParticipantId => "Assistant";
}