using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Live2D.App;

namespace PersonaEngine.Lib.Live2D.Behaviour;

/// <summary>
///     Base class for Live2D animation services, providing shared lifecycle
///     management (Start/Stop/Dispose) so subclasses only implement domain logic.
/// </summary>
public abstract class AnimationServiceBase : ILive2DAnimationService
{
    private bool _disposed;

    protected AnimationServiceBase(ILogger logger)
    {
        Logger = logger;
    }

    protected ILogger Logger { get; }

    protected LAppModel? Model { get; private set; }

    internal bool IsStarted { get; set; }

    public void Start(LAppModel model)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Model = model ?? throw new ArgumentNullException(nameof(model));
        OnStarting();
        IsStarted = true;
        Logger.LogInformation("{Service} started", GetType().Name);
    }

    public void Stop()
    {
        if (!IsStarted)
        {
            return;
        }

        IsStarted = false;
        OnStopping();
        Logger.LogDebug("{Service} stopped", GetType().Name);
    }

    public abstract void Update(float deltaTime);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        OnDisposing();
        Model = null;
        GC.SuppressFinalize(this);
    }

    protected virtual void OnStarting() { }

    protected virtual void OnStopping() { }

    protected virtual void OnDisposing() { }
}
