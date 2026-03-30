namespace PersonaEngine.Lib.Utils;

public sealed class AsyncQueue<T> : IDisposable
{
    private readonly Queue<T> _queue = new();

    private readonly SemaphoreSlim _semaphore = new(0);

    private bool _isDisposed;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _semaphore.Dispose();
        _isDisposed = true;
    }

    public void Enqueue(T item)
    {
        lock (_queue)
        {
            _queue.Enqueue(item);
            _semaphore.Release();
        }
    }

    public async Task<T> DequeueAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);

        lock (_queue)
        {
            return _queue.Dequeue();
        }
    }
}
