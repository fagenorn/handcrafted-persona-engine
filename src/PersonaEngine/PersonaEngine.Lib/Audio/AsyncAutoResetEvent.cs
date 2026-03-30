namespace PersonaEngine.Lib.Audio;

internal class AsyncAutoResetEvent
{
    private static readonly Task Completed = Task.CompletedTask;

    private int isSignaled; // 0 for false, 1 for true

    private TaskCompletionSource<bool>? waitTcs;

    public Task WaitAsync()
    {
        if (Interlocked.CompareExchange(ref isSignaled, 0, 1) == 1)
        {
            return Completed;
        }

        var tcs = new TaskCompletionSource<bool>();
        var oldTcs = Interlocked.Exchange(ref waitTcs, tcs);
        oldTcs?.TrySetCanceled();

        return tcs.Task;
    }

    public void Set()
    {
        var toRelease = Interlocked.Exchange(ref waitTcs, null);
        if (toRelease != null)
        {
            toRelease.SetResult(true);
        }
        else
        {
            Interlocked.Exchange(ref isSignaled, 1);
        }
    }
}
