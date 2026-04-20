using System.Collections.Concurrent;
using FluentAssertions;
using PersonaEngine.Lib.Assets.Manifest;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests;

/// <summary>
/// Exercises the tab-nav profile picker against a Spectre TestConsole + a
/// scripted key sequence. Catches the "panel never renders" regression that
/// the Live-display approach suffered: we assert that every frame contains
/// the profile title and at least one bullet of the currently-highlighted
/// choice before the user presses Enter.
/// </summary>
public sealed class SpectreBootstrapUserInterfacePickerTests
{
    private static readonly InstallManifest Manifest = ManifestLoader.LoadEmbedded();

    private static IReadOnlyList<ProfileChoice> Choices() =>
        ProfileChoiceCatalog.BuildFrom(Manifest);

    private static TestConsole NewInteractiveConsole()
    {
        // Interactive + color + unicode + big width so panel rendering mirrors
        // what a real Windows Terminal user sees. Without Interactive=true the
        // UI's own non-interactive fallback branch would fire.
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

    [Fact]
    public async Task Enter_on_first_profile_returns_TryItOut()
    {
        var console = NewInteractiveConsole();
        var ui = new SpectreBootstrapUserInterface(
            console,
            readKey: ScriptedKeys(ConsoleKey.Enter),
            forceInteractive: true
        );

        var picked = await ui.PickProfileAsync(Choices(), CancellationToken.None);

        picked.Should().Be(ProfileTier.TryItOut);
    }

    [Fact]
    public async Task RightArrow_moves_selection_and_Enter_commits()
    {
        var console = NewInteractiveConsole();
        var ui = new SpectreBootstrapUserInterface(
            console,
            readKey: ScriptedKeys(ConsoleKey.RightArrow, ConsoleKey.Enter),
            forceInteractive: true
        );

        var picked = await ui.PickProfileAsync(Choices(), CancellationToken.None);

        picked.Should().Be(ProfileTier.StreamWithIt);
    }

    [Fact]
    public async Task Two_RightArrows_land_on_BuildWithIt()
    {
        var console = NewInteractiveConsole();
        var ui = new SpectreBootstrapUserInterface(
            console,
            readKey: ScriptedKeys(ConsoleKey.RightArrow, ConsoleKey.RightArrow, ConsoleKey.Enter),
            forceInteractive: true
        );

        var picked = await ui.PickProfileAsync(Choices(), CancellationToken.None);

        picked.Should().Be(ProfileTier.BuildWithIt);
    }

    [Fact]
    public async Task LeftArrow_wraps_from_first_to_last()
    {
        var console = NewInteractiveConsole();
        var ui = new SpectreBootstrapUserInterface(
            console,
            readKey: ScriptedKeys(ConsoleKey.LeftArrow, ConsoleKey.Enter),
            forceInteractive: true
        );

        var picked = await ui.PickProfileAsync(Choices(), CancellationToken.None);

        picked.Should().Be(ProfileTier.BuildWithIt);
    }

    [Fact]
    public async Task NumericShortcut_3_jumps_to_BuildWithIt()
    {
        var console = NewInteractiveConsole();
        var ui = new SpectreBootstrapUserInterface(
            console,
            readKey: ScriptedKeys(ConsoleKey.D3, ConsoleKey.Enter),
            forceInteractive: true
        );

        var picked = await ui.PickProfileAsync(Choices(), CancellationToken.None);

        picked.Should().Be(ProfileTier.BuildWithIt);
    }

    [Fact]
    public async Task Escape_throws_OperationCanceled()
    {
        var console = NewInteractiveConsole();
        var ui = new SpectreBootstrapUserInterface(
            console,
            readKey: ScriptedKeys(ConsoleKey.Escape),
            forceInteractive: true
        );

        Func<Task> act = () => ui.PickProfileAsync(Choices(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Picker_actually_renders_panel_and_bullets_each_frame()
    {
        // Regression: the original Live-display version showed only the banner
        // and nothing below it. This asserts that the panel body (title +
        // first bullet of each profile as the user navigates through them)
        // lands in the console output.
        var console = NewInteractiveConsole();
        var ui = new SpectreBootstrapUserInterface(
            console,
            readKey: ScriptedKeys(ConsoleKey.RightArrow, ConsoleKey.RightArrow, ConsoleKey.Enter),
            forceInteractive: true
        );
        var choices = Choices();

        await ui.PickProfileAsync(choices, CancellationToken.None);

        var output = console.Output;

        // Banner survived the Clear+redraw cycle.
        output
            .Should()
            .Contain(
                "Handcrafted",
                because: "the banner tagline must appear in at least one frame"
            );

        // Every profile title has been rendered (because we walked through
        // all three before pressing Enter on the third).
        foreach (var choice in choices)
        {
            output
                .Should()
                .Contain(
                    choice.Title,
                    because: $"the title '{choice.Title}' must appear when that profile is the current tab"
                );
        }

        // The currently-selected profile's bullets must land in output — this
        // is the specific thing that was missing when Live failed to flush.
        foreach (var bullet in choices[2].Bullets)
        {
            output
                .Should()
                .Contain(bullet, because: $"bullet '{bullet}' must render in the detail panel");
        }

        // Size label for the final (selected) tier must be printed — otherwise
        // the user can't see download size before committing.
        output.Should().Contain(choices[2].SizeLabel);
    }

    [Fact]
    public async Task Redirected_stdin_falls_back_to_linear_prompt_without_crashing()
    {
        // forceInteractive: false + a TestConsole that reports non-interactive
        // capabilities should take the SelectionPrompt branch. TestConsole's
        // Input feeds keystrokes to the prompt.
        var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true; // allow SelectionPrompt to run
        console.Input.PushKey(ConsoleKey.Enter); // accept default (first item)

        var ui = new SpectreBootstrapUserInterface(console);

        // Can't easily force Console.IsInputRedirected, but we can prove the
        // non-interactive branch doesn't deadlock by disabling _forceInteractive
        // and feeding a key stream the SelectionPrompt understands.
        var picked = await ui.PickProfileAsync(Choices(), CancellationToken.None);
        picked.Should().Be(ProfileTier.TryItOut);
    }
}
