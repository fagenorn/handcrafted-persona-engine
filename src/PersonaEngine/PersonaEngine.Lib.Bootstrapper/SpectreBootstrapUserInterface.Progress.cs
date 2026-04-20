using System.Globalization;
using PersonaEngine.Lib.Bootstrapper.Planner;
using PersonaEngine.Lib.Bootstrapper.Sources;
using Spectre.Console;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>
/// Download progress UI and final result panel. Renders a per-asset progress bar
/// and the success/failure summary at the end of the run.
/// </summary>
public sealed partial class SpectreBootstrapUserInterface
{
    public void ShowPlanSummary(AssetPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var totalMb = plan.RequiredBytes / 1024.0 / 1024.0;
        var sizeLabel = totalMb >= 1024 ? $"{totalMb / 1024.0:N2} GB" : $"{totalMb:N0} MB";
        _console.WriteLine();
        _console.MarkupLine(
            $"[bold {AccentPrimary}]\u203a[/] [{TextPrimary}]Installing[/] [bold {AccentSecondary}]{plan.Items.Count}[/] [{TextPrimary}]asset(s),[/] [bold {AccentSecondary}]~{sizeLabel}[/]."
        );
        _console.WriteLine();
    }

    public async Task<bool> RunWithProgressAsync(
        IReadOnlyList<AssetPlanItem> items,
        Func<AssetPlanItem, IProgress<DownloadProgress>, CancellationToken, Task> executeOne,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(executeOne);

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
                    new DownloadedColumn { Culture = CultureInfo.InvariantCulture },
                    new TransferSpeedColumn { Culture = CultureInfo.InvariantCulture },
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
                            $"[bold {Error}]\u2717 {item.Entry.DisplayName}[/] [{TextSecondary}]failed:[/] {ex.Message}"
                        );
                    }
                }
            })
            .ConfigureAwait(false);

        return allOk;
    }

    public void ShowResult(BootstrapResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _console.WriteLine();
        if (result.Success)
        {
            var panel = new Panel(
                new Markup(
                    $"[bold {Success}]\u2713 Setup complete[/]\n\n"
                        + $"[{TextSecondary}]Active profile:[/] [bold {AccentPrimary}]{result.ActiveProfile}[/]\n"
                        + $"[{TextTertiary}]The app will continue starting up\u2026[/]"
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
                    $"[bold {Error}]\u2717 Setup failed[/]\n\n"
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
}
