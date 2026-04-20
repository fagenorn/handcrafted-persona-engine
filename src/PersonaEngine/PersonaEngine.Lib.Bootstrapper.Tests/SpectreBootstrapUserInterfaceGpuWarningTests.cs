using System.Collections.Concurrent;
using FluentAssertions;
using PersonaEngine.Lib.Bootstrapper.GpuPreflight;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests;

/// <summary>
/// Covers <see cref="SpectreBootstrapUserInterface.ShowGpuWarningAsync"/>
/// against a Spectre TestConsole + scripted key sequence. We lock in that the
/// warning panel actually renders (no "nothing but the header" regression) and
/// that C/Enter/Y continue while Q/N/Esc abort.
/// </summary>
public sealed class SpectreBootstrapUserInterfaceGpuWarningTests
{
    private static TestConsole NewInteractiveConsole()
    {
        var c = new TestConsole();
        c.Profile.Capabilities.Interactive = true;
        c.Profile.Capabilities.Ansi = true;
        c.Profile.Capabilities.Unicode = true;
        c.Profile.Capabilities.ColorSystem = ColorSystem.TrueColor;
        c.Profile.Width = 120;
        c.Profile.Height = 60;
        return c;
    }

    private static Func<CancellationToken, Task<ConsoleKeyInfo>> ScriptedKeys(
        params ConsoleKey[] keys
    )
    {
        var queue = new ConcurrentQueue<ConsoleKey>(keys);
        return (_) =>
        {
            if (!queue.TryDequeue(out var k))
                throw new InvalidOperationException("Test ran out of scripted keys.");
            return Task.FromResult(
                new ConsoleKeyInfo('\0', k, shift: false, alt: false, control: false)
            );
        };
    }

    private static GpuStatus NoDriverStatus() =>
        GpuStatus.Fail(
            GpuFailureKind.NoDriver,
            "No NVIDIA driver detected. Install the latest driver."
        );

    private static GpuStatus DriverTooOldStatus() =>
        GpuStatus.Fail(
            GpuFailureKind.DriverTooOld,
            "Driver 560.35 is older than 580.65.",
            driver: "560.35",
            name: "NVIDIA GeForce RTX 3080"
        );

    [Theory]
    [InlineData(ConsoleKey.C)]
    [InlineData(ConsoleKey.Y)]
    [InlineData(ConsoleKey.Enter)]
    public async Task Continue_keys_return_Continue(ConsoleKey key)
    {
        var console = NewInteractiveConsole();
        var ui = new SpectreBootstrapUserInterface(
            console,
            readKey: ScriptedKeys(key),
            forceInteractive: true
        );

        var response = await ui.ShowGpuWarningAsync(NoDriverStatus(), CancellationToken.None);

        response.Should().Be(GpuWarningResponse.Continue);
    }

    [Theory]
    [InlineData(ConsoleKey.Q)]
    [InlineData(ConsoleKey.N)]
    [InlineData(ConsoleKey.Escape)]
    public async Task Abort_keys_return_Abort(ConsoleKey key)
    {
        var console = NewInteractiveConsole();
        var ui = new SpectreBootstrapUserInterface(
            console,
            readKey: ScriptedKeys(key),
            forceInteractive: true
        );

        var response = await ui.ShowGpuWarningAsync(DriverTooOldStatus(), CancellationToken.None);

        response.Should().Be(GpuWarningResponse.Abort);
    }

    [Fact]
    public async Task Warning_panel_actually_renders_headline_and_details()
    {
        // Regression guard: the panel must actually flush to the console
        // before the prompt waits for input. If it doesn't, the user sees a
        // blank screen and can't know what the warning is about.
        var console = NewInteractiveConsole();
        var ui = new SpectreBootstrapUserInterface(
            console,
            readKey: ScriptedKeys(ConsoleKey.Q),
            forceInteractive: true
        );

        var status = DriverTooOldStatus();
        await ui.ShowGpuWarningAsync(status, CancellationToken.None);

        var output = console.Output;
        output.Should().Contain("GPU preflight");
        output.Should().Contain("driver"); // headline mentions it
        output.Should().Contain(status.DriverVersion!);
        output.Should().Contain(status.GpuName!);
        output.Should().Contain("continue"); // hint row
        output.Should().Contain("quit");
    }

    [Fact]
    public async Task Unknown_keys_are_ignored_until_a_valid_choice()
    {
        // Extra keys before a real answer must not change the outcome — the
        // handler loops until it sees one it understands.
        var console = NewInteractiveConsole();
        var ui = new SpectreBootstrapUserInterface(
            console,
            readKey: ScriptedKeys(ConsoleKey.Spacebar, ConsoleKey.A, ConsoleKey.F5, ConsoleKey.C),
            forceInteractive: true
        );

        var response = await ui.ShowGpuWarningAsync(NoDriverStatus(), CancellationToken.None);

        response.Should().Be(GpuWarningResponse.Continue);
    }

    [Fact]
    public async Task ComputeTooLow_guidance_mentions_Pascal_floor()
    {
        var console = NewInteractiveConsole();
        var ui = new SpectreBootstrapUserInterface(
            console,
            readKey: ScriptedKeys(ConsoleKey.Q),
            forceInteractive: true
        );

        var status = GpuStatus.Fail(
            GpuFailureKind.ComputeTooLow,
            "Compute 5.2 below 6.0.",
            driver: "580.70",
            name: "NVIDIA Quadro M2000",
            cc: (5, 2)
        );

        await ui.ShowGpuWarningAsync(status, CancellationToken.None);

        // The headline should reference Pascal as the supported floor so the
        // user knows which architectures are safe to upgrade to.
        console.Output.Should().Contain("Pascal");
    }
}
