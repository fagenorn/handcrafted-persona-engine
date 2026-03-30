namespace PersonaEngine.Lib.Vision;

public interface IVisualQAService : IAsyncDisposable
{
    string? ScreenCaption { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();
}
