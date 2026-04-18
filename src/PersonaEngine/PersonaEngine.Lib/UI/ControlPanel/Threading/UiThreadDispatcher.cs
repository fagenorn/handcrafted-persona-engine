using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PersonaEngine.Lib.UI.ControlPanel.Threading;

public sealed class UiThreadDispatcher(ILogger<UiThreadDispatcher> logger) : IUiThreadDispatcher
{
    internal const int MaxPerFrame = 100;

    private readonly ConcurrentQueue<Action> _queue = new();

    public void Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _queue.Enqueue(work);
    }

    public void DrainPending()
    {
        for (var i = 0; i < MaxPerFrame; i++)
        {
            if (!_queue.TryDequeue(out var work))
            {
                return;
            }

            try
            {
                work();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "UiThreadDispatcher swallowed exception from queued action.");
            }
        }
    }
}
