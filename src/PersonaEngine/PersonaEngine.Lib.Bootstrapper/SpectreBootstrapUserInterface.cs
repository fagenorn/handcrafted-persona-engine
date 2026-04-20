using Spectre.Console;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>
/// Spectre.Console-based UI for the bootstrap flow. Color palette is kept in lockstep
/// with the in-app theme (see <c>UI/ControlPanel/Layout/Theme.cs</c>) so the CLI feels
/// like a continuation of the running app instead of a generic installer.
/// Split across partial files by concern: this file holds construction + input plumbing,
/// <c>.Theme.cs</c> holds the palette + banner, <c>.Picker.cs</c> holds profile selection,
/// <c>.GpuWarning.cs</c> holds the GPU preflight panel, <c>.Progress.cs</c> holds the
/// download progress UI and final result panel.
/// </summary>
public sealed partial class SpectreBootstrapUserInterface : IBootstrapUserInterface
{
    private readonly IAnsiConsole _console;
    private readonly Func<CancellationToken, Task<ConsoleKeyInfo>> _readKey;
    private readonly bool _forceInteractive;

    public SpectreBootstrapUserInterface(IAnsiConsole? console = null)
        : this(console, readKey: null, forceInteractive: false) { }

    // Test seam: inject a key-reader and skip the Console.IsInputRedirected gate
    // so the tab-nav loop can run against TestConsole. Production callers use
    // the single-arg ctor above.
    internal SpectreBootstrapUserInterface(
        IAnsiConsole? console,
        Func<CancellationToken, Task<ConsoleKeyInfo>>? readKey,
        bool forceInteractive
    )
    {
        _console = console ?? AnsiConsole.Console;
        _readKey = readKey ?? DefaultReadKeyAsync;
        _forceInteractive = forceInteractive;
    }

    /// <summary>
    ///     True when we can drive a key-event loop: the operator isn't piping input
    ///     in and the terminal advertises interactive capabilities. The
    ///     <c>_forceInteractive</c> test seam bypasses both checks so TestConsole
    ///     flows can exercise the interactive paths.
    /// </summary>
    private bool IsInteractiveTerminal() =>
        _forceInteractive
        || (!Console.IsInputRedirected && _console.Profile.Capabilities.Interactive);

    private static async Task<ConsoleKeyInfo> DefaultReadKeyAsync(CancellationToken ct)
    {
        // Poll Console.KeyAvailable so cancellation can interrupt the wait —
        // a blocking Console.ReadKey would swallow the token.
        while (!Console.KeyAvailable)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(20, ct).ConfigureAwait(false);
        }
        return Console.ReadKey(intercept: true);
    }

    /// <summary>
    ///     Maps a <see cref="ConsoleKey" /> representing a digit 1..9 to a zero-based
    ///     choice index, honoring both the top-row D-keys and the numpad. Returns
    ///     false for any other key so the caller can ignore it.
    /// </summary>
    internal static bool TryNumberKeyToIndex(ConsoleKey key, int choiceCount, out int index)
    {
        // ConsoleKey values: D0=48..D9=57, NumPad0=96..NumPad9=105.
        // One range check per group beats a ten-arm switch and scales past 3 profiles.
        var digit =
            key is >= ConsoleKey.D1 and <= ConsoleKey.D9 ? key - ConsoleKey.D0
            : key is >= ConsoleKey.NumPad1 and <= ConsoleKey.NumPad9 ? key - ConsoleKey.NumPad0
            : 0;

        if (digit >= 1 && digit <= choiceCount)
        {
            index = digit - 1;
            return true;
        }

        index = -1;
        return false;
    }
}
