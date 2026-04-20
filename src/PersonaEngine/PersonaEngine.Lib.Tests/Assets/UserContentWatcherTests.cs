using FluentAssertions;
using PersonaEngine.Lib.Assets;
using Xunit;

namespace PersonaEngine.Lib.Tests.Assets;

public class UserContentWatcherTests : IDisposable
{
    private readonly string _root;

    public UserContentWatcherTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "UserContentWatcherTests-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Fires_Changed_when_file_added()
    {
        var fired = new TaskCompletionSource();
        using var watcher = new UserContentWatcher(_root, debounce: TimeSpan.FromMilliseconds(50));
        watcher.Changed += (_, _) => fired.TrySetResult();

        File.WriteAllText(Path.Combine(_root, "new.bin"), "x");

        // Timeout is generous: FileSystemWatcher first-event delivery on a
        // loaded Windows CI runner has been observed to exceed the original
        // 2s, even though locally it fires in <100ms. 10s still gives us a
        // hard upper bound on "the watcher is wired up at all".
        var done = await Task.WhenAny(fired.Task, Task.Delay(10_000));
        done.Should().Be(fired.Task, "the watcher should fire once a file is created");
    }

    [Fact]
    public async Task Debounces_burst_writes_into_a_single_event()
    {
        var fireCount = 0;
        using var watcher = new UserContentWatcher(_root, debounce: TimeSpan.FromMilliseconds(300));
        watcher.Changed += (_, _) => Interlocked.Increment(ref fireCount);

        for (var i = 0; i < 20; i++)
            File.WriteAllText(Path.Combine(_root, $"f{i}.bin"), i.ToString());

        await Task.Delay(1000);

        fireCount.Should().Be(1, "20 quick writes should debounce to exactly one Changed event");
    }
}
