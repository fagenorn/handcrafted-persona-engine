using PersonaEngine.Lib.Bootstrapper.Manifest;
using PersonaEngine.Lib.Bootstrapper.Planner;
using Spectre.Console;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>
/// Full Spectre.Console-backed implementation of <see cref="IBootstrapUserInterface"/>.
/// Renders an interactive profile picker and per-asset download progress bars.
/// </summary>
public sealed class SpectreBootstrapUserInterface : IBootstrapUserInterface
{
    /// <inheritdoc/>
    public Task<ProfileTier> PickProfileAsync(
        IReadOnlyList<ProfileChoice> choices,
        CancellationToken ct
    )
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Welcome to Persona Engine![/]");
        AnsiConsole.MarkupLine("Select an install profile to continue.");
        AnsiConsole.WriteLine();

        var prompt = new SelectionPrompt<ProfileChoice>()
            .Title("[bold yellow]Install profile[/]")
            .PageSize(10)
            .UseConverter(c =>
                $"[bold]{Markup.Escape(c.Title)}[/]  [dim]{Markup.Escape(c.SizeLabel)}[/]  — {Markup.Escape(c.Tagline)}"
            )
            .AddChoices(choices);

        var choice = AnsiConsole.Prompt(prompt);
        return Task.FromResult(choice.Profile);
    }

    /// <inheritdoc/>
    public void ShowPlanSummary(AssetPlan plan)
    {
        AnsiConsole.WriteLine();

        var downloadItems = plan
            .Items.Where(i => i.Action is AssetAction.Download or AssetAction.Redownload)
            .ToList();

        if (downloadItems.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]Nothing to download — all assets are up to date.[/]");
            return;
        }

        var totalGb = plan.RequiredBytes / 1_073_741_824.0;
        AnsiConsole.MarkupLine(
            $"[bold]Downloading {downloadItems.Count} asset(s) — [yellow]{totalGb:F1} GB[/] total[/]"
        );
        AnsiConsole.WriteLine();
    }

    /// <inheritdoc/>
    public async Task<bool> RunWithProgressAsync(
        IReadOnlyList<AssetPlanItem> items,
        Func<AssetPlanItem, IProgress<long>, CancellationToken, Task> executeOne,
        CancellationToken ct
    )
    {
        var allOk = true;

        await AnsiConsole
            .Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new TransferSpeedColumn(),
                new SpinnerColumn()
            )
            .StartAsync(async ctx =>
            {
                var tasks = items
                    .Select(item =>
                        (
                            Item: item,
                            Task: ctx.AddTask(
                                Markup.Escape(item.Entry.DisplayName),
                                maxValue: item.Entry.SizeBytes > 0 ? item.Entry.SizeBytes : 1
                            )
                        )
                    )
                    .ToList();

                foreach (var (item, progressTask) in tasks)
                {
                    progressTask.StartTask();

                    var progress = new Progress<long>(bytesReceived =>
                    {
                        progressTask.Value = bytesReceived;
                    });

                    try
                    {
                        await executeOne(item, progress, ct);
                        progressTask.Value = progressTask.MaxValue;
                        progressTask.StopTask();
                    }
                    catch (OperationCanceledException)
                    {
                        progressTask.StopTask();
                        throw;
                    }
                    catch (Exception ex)
                    {
                        progressTask.StopTask();
                        allOk = false;
                        AnsiConsole.MarkupLine(
                            $"[red]  Failed:[/] {Markup.Escape(item.Entry.DisplayName)}: {Markup.Escape(ex.Message)}"
                        );
                    }
                }
            });

        return allOk;
    }

    /// <inheritdoc/>
    public void ShowResult(BootstrapResult result)
    {
        AnsiConsole.WriteLine();

        if (result.Success)
        {
            var verb = result.ChangesApplied ? "installed" : "verified";
            AnsiConsole.MarkupLine(
                $"[bold green]Done![/] Profile [bold]{Markup.Escape(result.ActiveProfile.ToString())}[/] {verb} successfully."
            );
        }
        else
        {
            AnsiConsole.MarkupLine(
                $"[bold red]Bootstrap failed.[/] {Markup.Escape(result.ErrorMessage ?? "Unknown error.")}"
            );
        }

        if (result.Warnings.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Warnings:[/]");

            foreach (var w in result.Warnings)
                AnsiConsole.MarkupLine($"  [yellow]•[/] {Markup.Escape(w)}");
        }

        AnsiConsole.WriteLine();
    }
}
