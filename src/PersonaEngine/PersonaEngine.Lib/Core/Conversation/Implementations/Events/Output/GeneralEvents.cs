using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;

public record ErrorOutputEvent(Guid SessionId, Guid? TurnId, DateTimeOffset Timestamp, Exception Exception) : IOutputEvent { }