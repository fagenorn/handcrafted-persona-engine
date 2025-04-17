using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;

public record LlmStreamStartEvent(Guid SessionId, Guid? TurnId, DateTimeOffset Timestamp) : IOutputEvent { }

public record LlmChunkEvent(Guid SessionId, Guid? TurnId, DateTimeOffset Timestamp, string Chunk) : IOutputEvent { }

public record LlmStreamEndEvent(Guid SessionId, Guid? TurnId, DateTimeOffset Timestamp, CompletionReason FinishReason) : IOutputEvent { }