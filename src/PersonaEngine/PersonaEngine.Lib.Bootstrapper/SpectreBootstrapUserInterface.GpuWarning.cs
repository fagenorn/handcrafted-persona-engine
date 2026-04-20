using System.Text;
using PersonaEngine.Lib.Bootstrapper.GpuPreflight;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>
/// GPU preflight warning panel. Interactive terminals get a Continue/Abort prompt;
/// non-interactive terminals render the panel and abort (matching NoOp behaviour).
/// </summary>
public sealed partial class SpectreBootstrapUserInterface
{
    public async Task<GpuWarningResponse> ShowGpuWarningAsync(
        GpuStatus status,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(status);

        // Non-interactive terminal: render the panel for log-scraping but
        // don't wait for a keypress — the user can't answer one. The runner
        // treats this as abort so we match the NoOp UI's behaviour.
        if (!IsInteractiveTerminal())
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

    private static IRenderable BuildGpuWarningView(GpuStatus status, bool interactive)
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
            body.AppendLine(string.Join($"   [{TextTertiary}]\u2022[/] ", details));
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
                $"  [{TextTertiary}]Non-interactive mode \u2014 aborting. Re-run with[/] "
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
}
