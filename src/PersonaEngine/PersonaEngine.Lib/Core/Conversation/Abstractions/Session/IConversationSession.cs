using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

public interface IConversationSession : IAsyncDisposable
{
    IConversationContext Context { get; }

    ConversationState CurrentState { get; }

    Guid SessionId { get; }

    ValueTask RunAsync(CancellationToken cancellationToken);

    ValueTask StopAsync();
}
