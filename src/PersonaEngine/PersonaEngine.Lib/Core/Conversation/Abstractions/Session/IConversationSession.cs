namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

public interface IConversationSession : IAsyncDisposable
{
    Guid SessionId { get; }
    
    ValueTask RunAsync(CancellationToken cancellationToken);

    ValueTask StopAsync();
}