using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;
using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;

public interface IAudioProgressEvent : IOutputEvent { }

public record AudioPlaybackEndedEvent(Guid SessionId, Guid? TurnId, DateTimeOffset Timestamp, CompletionReason FinishReason) : IOutputEvent { }

public record AudioPlaybackStartedEvent(Guid SessionId, Guid? TurnId, DateTimeOffset Timestamp) : IOutputEvent { }

public record AudioChunkPlaybackStartedEvent(
    Guid           SessionId,
    Guid?          TurnId,
    DateTimeOffset Timestamp,
    AudioSegment   Chunk
) : BaseOutputEvent(SessionId, TurnId, Timestamp), IAudioProgressEvent;

public record AudioChunkPlaybackEndedEvent(
    Guid           SessionId,
    Guid?          TurnId,
    DateTimeOffset Timestamp,
    AudioSegment   Chunk
) : BaseOutputEvent(SessionId, TurnId, Timestamp), IAudioProgressEvent;

public record AudioPlaybackProgressEvent(
    Guid           SessionId,
    Guid?          TurnId,
    DateTimeOffset Timestamp,
    TimeSpan       CurrentPlaybackTime
) : BaseOutputEvent(SessionId, TurnId, Timestamp), IAudioProgressEvent;