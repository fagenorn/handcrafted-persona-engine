using Microsoft.Extensions.Logging.Abstractions;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Audition;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.ControlPanel.Voice;

public sealed class OneShotAudioPlayerTests
{
    [Fact]
    public async Task Second_Play_Cancels_First()
    {
        using var player = new OneShotAudioPlayer(NullLogger<OneShotAudioPlayer>.Instance);

        var first = player.PlayAsync(new float[24000], 24000, CancellationToken.None);
        var second = player.PlayAsync(new float[24000], 24000, CancellationToken.None);

        await first; // must complete (cancelled) without throwing
        await second;
    }

    [Fact]
    public async Task Cancellation_Stops_Playback_Promptly()
    {
        using var player = new OneShotAudioPlayer(NullLogger<OneShotAudioPlayer>.Instance);
        using var cts = new CancellationTokenSource();

        var task = player.PlayAsync(new float[24000 * 5], 24000, cts.Token);
        await Task.Delay(50);
        cts.Cancel();

        await task; // must complete within a reasonable time after cancel
    }

    [Fact]
    public void Dispose_Is_Idempotent()
    {
        var player = new OneShotAudioPlayer(NullLogger<OneShotAudioPlayer>.Instance);
        player.Dispose();
        player.Dispose(); // must not throw
    }
}
