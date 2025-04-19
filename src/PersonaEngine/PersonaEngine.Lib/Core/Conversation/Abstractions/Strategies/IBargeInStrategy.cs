using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Strategies;

public record struct BargeInContext(
    ConversationOptions ConversationOptions,
    ConversationState CurrentState,
    IInputEvent       InputEvent,
    Guid              SessionId,
    Guid?             TurnId
);

public interface IBargeInStrategy
{
    bool ShouldAllowBargeIn(BargeInContext context);
}