namespace PersonaEngine.Lib.Core.Conversation.Detection;

public interface IBargeInDetector : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync();
}