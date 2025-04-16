using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;
using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;

public record TtsStreamStartEvent(Guid SessionId, Guid? TurnId, DateTimeOffset Timestamp) : IOutputEvent { }

public record TtsChunkEvent(Guid SessionId, Guid? TurnId, DateTimeOffset Timestamp, AudioSegment Chunk) : IOutputEvent { }

public record TtsStreamEndEvent(Guid SessionId, Guid? TurnId, DateTimeOffset Timestamp, CompletionReason FinishReason) : IOutputEvent { }