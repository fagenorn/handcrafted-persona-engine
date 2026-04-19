namespace PersonaEngine.Lib.Assets;

public sealed class UserContentWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly TimeSpan _debounce;
    private CancellationTokenSource? _pendingFire;
    private readonly object _gate = new();

    public event EventHandler? Changed;

    public string Root { get; }

    public UserContentWatcher(string root, TimeSpan debounce)
    {
        Root = root;
        _debounce = debounce;
        Directory.CreateDirectory(root);

        _watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter =
                NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Created += (_, _) => Schedule();
        _watcher.Deleted += (_, _) => Schedule();
        _watcher.Renamed += (_, _) => Schedule();
        _watcher.Changed += (_, _) => Schedule();
    }

    private void Schedule()
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            _pendingFire?.Cancel();
            _pendingFire = new CancellationTokenSource();
            cts = _pendingFire;
        }

        Task.Delay(_debounce, cts.Token)
            .ContinueWith(
                t =>
                {
                    if (t.IsCanceled)
                        return;
                    try
                    {
                        Changed?.Invoke(this, EventArgs.Empty);
                    }
                    catch
                    {
                        /* swallow listener errors so one bad handler doesn't break the watcher */
                    }
                },
                TaskScheduler.Default
            );
    }

    public void Dispose()
    {
        lock (_gate)
            _pendingFire?.Cancel();
        _watcher.Dispose();
    }
}
