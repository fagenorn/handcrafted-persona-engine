using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace PersonaEngine.Lib.UI.ControlPanel.Threading;

public sealed class UiThreadDispatcher(ILogger<UiThreadDispatcher> logger) : IUiThreadDispatcher
{
    internal const int MaxPerFrame = 64;

    /// <summary>
    ///     Maximum queue depth before <see cref="Post" /> starts dropping the oldest
    ///     pending action. Generous for normal operation but bounds captured-state
    ///     memory under a probe-storm or TTS-error cascade.
    /// </summary>
    internal const int MaxQueueDepth = 256;

    /// <summary>Log one warning every time the drop tally crosses this many additional drops.</summary>
    private const long DropLogInterval = 100;

    private readonly ConcurrentQueue<Action> _queue = new();

    private long _totalDropped;
    private long _lastLoggedDropMilestone;

    /// <summary>Current number of pending actions. Diagnostic-only; may race with concurrent Post/Drain.</summary>
    public int QueueDepth => _queue.Count;

    /// <summary>Monotonically increasing count of actions dropped due to the queue-depth cap.</summary>
    public long TotalDropped => Interlocked.Read(ref _totalDropped);

    public void Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);

        // Drop-oldest: if we're already at the cap, evict the eldest pending action
        // before enqueuing the new one. A single dequeue is sufficient because Post
        // is the only producer of growth and we cap each call to a single drop.
        if (_queue.Count >= MaxQueueDepth)
        {
            if (_queue.TryDequeue(out _))
            {
                var total = Interlocked.Increment(ref _totalDropped);
                MaybeLogDropMilestone(total);
            }
        }

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

    // Rate-limited warning: emit once per DropLogInterval drops so a sustained storm
    // doesn't flood the log while still surfacing that drops are occurring.
    private void MaybeLogDropMilestone(long total)
    {
        var milestone = total / DropLogInterval;
        var previous = Interlocked.Read(ref _lastLoggedDropMilestone);
        if (milestone <= previous)
        {
            return;
        }

        // Only the thread that actually advances the milestone emits the log.
        if (
            Interlocked.CompareExchange(ref _lastLoggedDropMilestone, milestone, previous)
            == previous
        )
        {
            logger.LogWarning(
                "UiThreadDispatcher dropped {TotalDropped} queued actions (cap={Cap}). Investigate producer-side back-pressure.",
                total,
                MaxQueueDepth
            );
        }
    }
}
