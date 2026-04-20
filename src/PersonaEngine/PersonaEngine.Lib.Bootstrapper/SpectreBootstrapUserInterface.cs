using System.Text;
using PersonaEngine.Lib.Assets.Manifest;
using PersonaEngine.Lib.Bootstrapper.GpuPreflight;
using PersonaEngine.Lib.Bootstrapper.Planner;
using PersonaEngine.Lib.Bootstrapper.Sources;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>
/// Spectre.Console-based UI for the bootstrap flow. Color palette is kept in lockstep
/// with the in-app theme (see <c>UI/ControlPanel/Layout/Theme.cs</c>) so the CLI feels
/// like a continuation of the running app instead of a generic installer.
/// </summary>
public sealed class SpectreBootstrapUserInterface : IBootstrapUserInterface
{
    // Theme palette mirrors UI/ControlPanel/Layout/Theme.cs. Hex literals so
    // we never accidentally drift from the in-app colors when one is tweaked.
    private const string AccentPrimary = "#e8a0bf"; // soft pink
    private const string AccentSecondary = "#b68fd0"; // lavender
    private const string Success = "#82d4a8"; // mint
    private const string Warning = "#f0ca78"; // amber
    private const string Error = "#e87878"; // coral
    private const string TextPrimary = "#f3eaf2";
    private const string TextSecondary = "#d4b8d8";
    private const string TextTertiary = "#9a8aa3";

    // Pre-parsed Spectre.Console Color values for the palette above. Spectre's
    // Color ctor takes raw RGB bytes — we only convert once at type init.
    private static readonly Color AccentPrimaryColor = Hex(AccentPrimary);
    private static readonly Color AccentSecondaryColor = Hex(AccentSecondary);
    private static readonly Color SuccessColor = Hex(Success);
    private static readonly Color ErrorColor = Hex(Error);
    private static readonly Color RemainingTrackColor = Hex("#3a2a3f");

    private static Color Hex(string hex)
    {
        var s = hex.AsSpan().TrimStart('#');
        var r = byte.Parse(s[..2], System.Globalization.NumberStyles.HexNumber);
        var g = byte.Parse(s[2..4], System.Globalization.NumberStyles.HexNumber);
        var b = byte.Parse(s[4..6], System.Globalization.NumberStyles.HexNumber);
        return new Color(r, g, b);
    }

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

    public async Task<ProfileTier> PickProfileAsync(
        IReadOnlyList<ProfileChoice> choices,
        CancellationToken ct
    )
    {
        // Non-interactive fallback (redirected stdin, dumb terminals): fall
        // back to Spectre's linear SelectionPrompt so CI jobs and piped input
        // still work. The tab-nav loop below depends on Console.ReadKey.
        if (
            !_forceInteractive
            && (Console.IsInputRedirected || !_console.Profile.Capabilities.Interactive)
        )
        {
            RenderBanner();
            var prompt = new SelectionPrompt<ProfileChoice>()
                .Title($"[bold {AccentPrimary}]?[/] [{TextPrimary}]Pick an install profile[/]")
                .HighlightStyle(
                    new Style(foreground: AccentPrimaryColor, decoration: Decoration.Bold)
                )
                .UseConverter(c =>
                    $"[{TextPrimary}]{c.Title}[/] [{TextTertiary}]({c.SizeLabel})[/]"
                )
                .AddChoices(choices);

            return _console.Prompt(prompt).Profile;
        }

        var index = 0;

        // Manual redraw loop — Spectre's Live display does not play well with
        // a Console.ReadKey-driven tab picker (the initial target only shows
        // after the first internal refresh tick, which made the panel never
        // render). Clearing and re-writing the full view on every keypress is
        // a few bytes of flicker but deterministic across Windows Terminal,
        // conhost, ConEmu, and IDE-embedded terminals.
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            _console.Clear();
            RenderBanner();
            _console.Write(BuildPickerView(choices, index));

            var key = await _readKey(ct).ConfigureAwait(false);
            switch (key.Key)
            {
                case ConsoleKey.LeftArrow:
                case ConsoleKey.UpArrow:
                case ConsoleKey.H:
                case ConsoleKey.K:
                    index = (index - 1 + choices.Count) % choices.Count;
                    break;

                case ConsoleKey.RightArrow:
                case ConsoleKey.DownArrow:
                case ConsoleKey.Tab:
                case ConsoleKey.L:
                case ConsoleKey.J:
                    index = (index + 1) % choices.Count;
                    break;

                case ConsoleKey.Enter:
                case ConsoleKey.Spacebar:
                    // Clear the picker so the plan summary / download progress
                    // renders on a fresh screen — nothing stale left over.
                    _console.Clear();
                    return choices[index].Profile;

                case ConsoleKey.Escape:
                    _console.Clear();
                    throw new OperationCanceledException("Profile selection cancelled by user.");

                default:
                    // Number-key shortcut: D1..D9 / NumPad1..9 jump to that choice.
                    // Written as a single range check so adding a 4th profile later
                    // doesn't need another switch arm.
                    if (TryNumberKeyToIndex(key.Key, choices.Count, out var picked))
                        index = picked;
                    break;
            }
        }
    }

    public async Task<GpuWarningResponse> ShowGpuWarningAsync(
        GpuStatus status,
        CancellationToken ct
    )
    {
        // Non-interactive terminal: render the panel for log-scraping but
        // don't wait for a keypress — the user can't answer one. The runner
        // treats this as abort so we match the NoOp UI's behaviour.
        if (
            !_forceInteractive
            && (Console.IsInputRedirected || !_console.Profile.Capabilities.Interactive)
        )
        {
            RenderBanner();
            _console.Write(BuildGpuWarningView(status, interactive: false));
            _console.WriteLine();
            return GpuWarningResponse.Abort;
        }

        // Interactive path: show the panel once, then loop on keys until the
        // user picks an answer. No manual redraws — the view is static so a
        // single render is fine.
        _console.Clear();
        RenderBanner();
        _console.Write(BuildGpuWarningView(status, interactive: true));

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var key = await _readKey(ct).ConfigureAwait(false);
            switch (key.Key)
            {
                case ConsoleKey.C:
                case ConsoleKey.Y:
                case ConsoleKey.Enter:
                    _console.Clear();
                    return GpuWarningResponse.Continue;

                case ConsoleKey.Q:
                case ConsoleKey.N:
                case ConsoleKey.Escape:
                    _console.Clear();
                    return GpuWarningResponse.Abort;
            }
        }
    }

    private IRenderable BuildGpuWarningView(GpuStatus status, bool interactive)
    {
        var (headline, guidance) = status.Kind switch
        {
            GpuFailureKind.NoDriver => (
                "We couldn't find an NVIDIA GPU.",
                "Persona Engine needs an NVIDIA GPU with CUDA support. Install the latest "
                    + "GeForce or Studio driver from [underline]nvidia.com/Download[/]."
            ),
            GpuFailureKind.DriverTooOld => (
                "Your NVIDIA driver is too old.",
                $"We bundle CUDA 13 which needs Windows driver {NvidiaGpuPreflightCheck.MinDriver.Major}."
                    + $"{NvidiaGpuPreflightCheck.MinDriver.Minor:00} or newer. Grab the latest from [underline]nvidia.com/Download[/]."
            ),
            GpuFailureKind.ComputeTooLow => (
                "Your GPU is below the supported floor.",
                $"We support Pascal ({NvidiaGpuPreflightCheck.MinCompute.Major}.{NvidiaGpuPreflightCheck.MinCompute.Minor}) and newer. "
                    + "Older cards can't run modern cuDNN reliably."
            ),
            GpuFailureKind.ToolMissing => (
                "Driver found, but we couldn't verify it.",
                "`nvidia-smi` isn't on PATH, so we can't read the driver version. "
                    + "If you know your driver is recent (580.65+) you can continue anyway."
            ),
            GpuFailureKind.UnknownGpu => (
                "We couldn't parse your driver version.",
                "Your GPU looks present but `nvidia-smi` returned unexpected output. "
                    + "Continuing is usually fine on modern driver builds."
            ),
            _ => ("GPU check failed.", status.Detail ?? "(no details)"),
        };

        var body = new StringBuilder();
        body.AppendLine($"[bold {Warning}]{Markup.Escape(headline)}[/]");
        body.AppendLine();
        if (!string.IsNullOrWhiteSpace(status.Detail))
        {
            body.AppendLine($"[{TextPrimary}]{Markup.Escape(status.Detail)}[/]");
            body.AppendLine();
        }
        body.AppendLine($"[{TextSecondary}]{guidance}[/]");

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(status.GpuName))
            details.Add(
                $"[{TextTertiary}]GPU:[/] [{TextPrimary}]{Markup.Escape(status.GpuName!)}[/]"
            );
        if (!string.IsNullOrWhiteSpace(status.DriverVersion))
            details.Add(
                $"[{TextTertiary}]Driver:[/] [{TextPrimary}]{Markup.Escape(status.DriverVersion!)}[/]"
            );
        if (status.ComputeCapability is { } cc)
            details.Add($"[{TextTertiary}]Compute:[/] [{TextPrimary}]{cc.Major}.{cc.Minor}[/]");
        if (details.Count > 0)
        {
            body.AppendLine();
            body.AppendLine(string.Join($"   [{TextTertiary}]•[/] ", details));
        }

        var panel = new Panel(new Markup(body.ToString().TrimEnd()))
        {
            Header = new PanelHeader($" [bold {Warning}]GPU preflight[/] "),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: Hex(Warning)),
            Padding = new Padding(2, 1, 2, 1),
        };

        if (!interactive)
        {
            var notice = new Markup(
                $"  [{TextTertiary}]Non-interactive mode — aborting. Re-run with[/] "
                    + $"[bold {AccentSecondary}]--skip-gpu-check[/] "
                    + $"[{TextTertiary}]to bypass this check.[/]"
            );
            return new Rows(panel, notice);
        }

        var hint = new Markup(
            $"  [bold {Success}]C[/]/[bold {Success}]Enter[/] [{TextSecondary}]continue anyway[/]   "
                + $"[bold {Error}]Q[/]/[bold {Error}]Esc[/] [{TextSecondary}]quit installer[/]"
        );
        return new Rows(panel, hint);
    }

    private IRenderable BuildPickerView(IReadOnlyList<ProfileChoice> choices, int selectedIndex)
    {
        // Tab bar — one cell per profile, the current one highlighted on a
        // pink background. Numeric hint (1/2/3) so users notice the shortcut.
        var tabs = new StringBuilder();
        for (var i = 0; i < choices.Count; i++)
        {
            var label = $" {i + 1}. {choices[i].Title} ";
            tabs.Append(
                i == selectedIndex
                    ? $"[bold black on {AccentPrimary}]{Markup.Escape(label)}[/]"
                    : $"[{TextSecondary}]{Markup.Escape(label)}[/]"
            );
            if (i < choices.Count - 1)
                tabs.Append($"[{TextTertiary}]│[/]");
        }

        var current = choices[selectedIndex];
        var body = new StringBuilder();
        body.AppendLine(tabs.ToString());
        body.AppendLine();
        body.AppendLine(
            $"[bold {AccentPrimary}]{Markup.Escape(current.Title)}[/]  [{TextTertiary}]({Markup.Escape(current.SizeLabel)})[/]"
        );
        body.AppendLine();
        body.AppendLine($"[italic {TextSecondary}]\u201C{Markup.Escape(current.Tagline)}\u201D[/]");
        body.AppendLine();
        foreach (var bullet in current.Bullets)
        {
            body.AppendLine($"  [{AccentPrimary}]•[/] [{TextPrimary}]{Markup.Escape(bullet)}[/]");
        }

        var panel = new Panel(new Markup(body.ToString().TrimEnd()))
        {
            Header = new PanelHeader($" [bold {AccentPrimary}]Choose a profile[/] "),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: AccentSecondaryColor),
            Padding = new Padding(2, 1, 2, 1),
        };

        var hint = new Markup(
            $"  [{TextTertiary}]←/→[/] [{TextSecondary}]navigate[/]   "
                + $"[{TextTertiary}]1/2/3[/] [{TextSecondary}]jump[/]   "
                + $"[{TextTertiary}]Enter[/] [{TextSecondary}]select[/]   "
                + $"[{TextTertiary}]Esc[/] [{TextSecondary}]cancel[/]"
        );

        return new Rows(panel, hint);
    }

    public void ShowPlanSummary(AssetPlan plan)
    {
        var totalMb = plan.RequiredBytes / 1024.0 / 1024.0;
        var sizeLabel = totalMb >= 1024 ? $"{totalMb / 1024.0:N2} GB" : $"{totalMb:N0} MB";
        _console.WriteLine();
        _console.MarkupLine(
            $"[bold {AccentPrimary}]›[/] [{TextPrimary}]Installing[/] [bold {AccentSecondary}]{plan.Items.Count}[/] [{TextPrimary}]asset(s),[/] [bold {AccentSecondary}]~{sizeLabel}[/]."
        );
        _console.WriteLine();
    }

    public async Task<bool> RunWithProgressAsync(
        IReadOnlyList<AssetPlanItem> items,
        Func<AssetPlanItem, IProgress<DownloadProgress>, CancellationToken, Task> executeOne,
        CancellationToken ct
    )
    {
        var allOk = true;

        await _console
            .Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new ProgressColumn[]
                {
                    new TaskDescriptionColumn { Alignment = Justify.Left },
                    new ProgressBarColumn
                    {
                        CompletedStyle = new Style(foreground: AccentPrimaryColor),
                        FinishedStyle = new Style(foreground: SuccessColor),
                        RemainingStyle = new Style(foreground: RemainingTrackColor),
                    },
                    new PercentageColumn
                    {
                        Style = new Style(foreground: Hex(TextSecondary)),
                        CompletedStyle = new Style(foreground: SuccessColor),
                    },
                    new DownloadedColumn
                    {
                        Culture = System.Globalization.CultureInfo.InvariantCulture,
                    },
                    new TransferSpeedColumn
                    {
                        Culture = System.Globalization.CultureInfo.InvariantCulture,
                    },
                    new RemainingTimeColumn(),
                }
            )
            .StartAsync(async ctx =>
            {
                foreach (var item in items)
                {
                    // Manifest size is the lower bound; for NVIDIA assets it's 0 because
                    // the real total only arrives once we resolve their runtime redist
                    // JSON. Default the bar to 1 so it doesn't render full-green-finished
                    // before any bytes flow, then upgrade MaxValue on the first progress
                    // tick that carries a real total.
                    var initialMax = item.Entry.SizeBytes > 0 ? item.Entry.SizeBytes : 1;
                    var task = ctx.AddTask(
                        $"[{TextPrimary}]{item.Entry.DisplayName}[/]",
                        maxValue: initialMax
                    );
                    var sawRealTotal = item.Entry.SizeBytes > 0;
                    var progress = new Progress<DownloadProgress>(p =>
                    {
                        if (p.TotalBytes > 0 && (!sawRealTotal || p.TotalBytes > task.MaxValue))
                        {
                            task.MaxValue = p.TotalBytes;
                            sawRealTotal = true;
                        }
                        task.Value = p.BytesDownloaded;
                    });
                    try
                    {
                        await executeOne(item, progress, ct).ConfigureAwait(false);
                        // Pin the bar to 100% even if the source under-reported total
                        // bytes (or never reported any).
                        if (task.MaxValue <= 0)
                        {
                            task.MaxValue = 1;
                        }
                        task.Value = task.MaxValue;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        allOk = false;
                        _console.MarkupLineInterpolated(
                            $"[bold {Error}]✗ {item.Entry.DisplayName}[/] [{TextSecondary}]failed:[/] {ex.Message}"
                        );
                    }
                }
            })
            .ConfigureAwait(false);

        return allOk;
    }

    public void ShowResult(BootstrapResult result)
    {
        _console.WriteLine();
        if (result.Success)
        {
            var panel = new Panel(
                new Markup(
                    $"[bold {Success}]✓ Setup complete[/]\n\n"
                        + $"[{TextSecondary}]Active profile:[/] [bold {AccentPrimary}]{result.ActiveProfile}[/]\n"
                        + $"[{TextTertiary}]The app will continue starting up…[/]"
                )
            )
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(foreground: SuccessColor),
                Padding = new Padding(2, 1, 2, 1),
            };
            _console.Write(panel);
        }
        else
        {
            var panel = new Panel(
                new Markup(
                    $"[bold {Error}]✗ Setup failed[/]\n\n"
                        + $"[{TextSecondary}]{Markup.Escape(result.ErrorMessage ?? "(no details)")}[/]"
                )
            )
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(foreground: ErrorColor),
                Padding = new Padding(2, 1, 2, 1),
            };
            _console.Write(panel);
        }
        _console.WriteLine();
    }

    private void RenderBanner()
    {
        // ASCII banner sized to fit a typical 80-col terminal. Pink sparkle/heart
        // garland sits underneath the lavender figlet — the two-tone fade mirrors
        // the in-app accent gradient.
        const string sparkleBottom = "  · ｡ ⋆ ✧ ⊹ ˙ · ♡ · ⋆ ⋆ · ♡ · ｡ ⋆ ✧ ⊹ ˙ · ♡ · ⋆ ⋆ · ♡ ·";
        const string banner = """
             ___                                           ___
            (  _`\                                        (  _`\                _
            | |_) )  __   _ __   ___    _     ___     _ _ | (_(_)  ___     __  (_)  ___     __
            | ,__/'/'__`\( '__)/',__) /'_`\ /' _ `\ /'_` )|  _)_ /' _ `\ /'_ `\| |/' _ `\ /'__`\
            | |   (  ___/| |   \__, \( (_) )| ( ) |( (_| || (_( )| ( ) |( (_) || || ( ) |(  ___/
            (_)   `\____)(_)   (____/`\___/'(_) (_)`\__,_)(____/'(_) (_)`\__  |(_)(_) (_)`\____)
                                                                        ( )_) |
                                                                         \___/'
            """;

        _console.MarkupLine($"[bold {AccentSecondary}]{Markup.Escape(banner)}[/]");
        _console.MarkupLine($"[{AccentPrimary}]{Markup.Escape(sparkleBottom)}[/]");
        _console.WriteLine();
        _console.MarkupLine(
            $"  [{TextSecondary}]Handcrafted, locally-run AI persona for VTubers, streamers and tinkerers.[/]"
        );
        _console.MarkupLine(
            $"  [{TextTertiary}]First-run setup — pick what you want to install.[/]"
        );
        _console.WriteLine();
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
