using System.Text;
using PersonaEngine.Lib.Assets.Manifest;
using PersonaEngine.Lib.Bootstrapper.Planner;
using Spectre.Console;

namespace PersonaEngine.Lib.Bootstrapper;

public sealed class SpectreBootstrapUserInterface : IBootstrapUserInterface
{
    private readonly IAnsiConsole _console;

    public SpectreBootstrapUserInterface(IAnsiConsole? console = null)
    {
        _console = console ?? AnsiConsole.Console;
    }

    public Task<ProfileTier> PickProfileAsync(
        IReadOnlyList<ProfileChoice> choices,
        CancellationToken ct
    )
    {
        _console.Write(new Rule("[bold]PersonaEngine — first run setup[/]").LeftJustified());
        _console.WriteLine();

        foreach (var choice in choices)
        {
            var panel = new Panel(BuildChoiceBody(choice))
            {
                Header = new PanelHeader(
                    $" [bold]{choice.Title}[/]  [grey]({choice.SizeLabel})[/] "
                ),
                Border = BoxBorder.Rounded,
            };
            _console.Write(panel);
        }

        var prompt = new SelectionPrompt<ProfileChoice>()
            .Title("Pick an install profile:")
            .UseConverter(c => $"{c.Title} ({c.SizeLabel})")
            .AddChoices(choices);

        var selected = _console.Prompt(prompt);
        return Task.FromResult(selected.Profile);
    }

    public void ShowPlanSummary(AssetPlan plan)
    {
        var totalMb = plan.RequiredBytes / 1024.0 / 1024.0;
        _console.MarkupLine(
            $"[bold]Installing[/] [green]{plan.Items.Count}[/] asset(s), ~{totalMb:N0} MB."
        );
    }

    public async Task<bool> RunWithProgressAsync(
        IReadOnlyList<AssetPlanItem> items,
        Func<AssetPlanItem, IProgress<long>, CancellationToken, Task> executeOne,
        CancellationToken ct
    )
    {
        var allOk = true;

        await _console
            .Progress()
            .AutoClear(false)
            .Columns(
                new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new DownloadedColumn(),
                    new TransferSpeedColumn(),
                    new RemainingTimeColumn(),
                }
            )
            .StartAsync(async ctx =>
            {
                foreach (var item in items)
                {
                    var task = ctx.AddTask(item.Entry.DisplayName, maxValue: item.Entry.SizeBytes);
                    var progress = new Progress<long>(bytes => task.Value = bytes);
                    try
                    {
                        await executeOne(item, progress, ct).ConfigureAwait(false);
                        task.Value = item.Entry.SizeBytes;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        allOk = false;
                        _console.MarkupLineInterpolated(
                            $"[red]✗ {item.Entry.DisplayName} failed:[/] {ex.Message}"
                        );
                    }
                }
            })
            .ConfigureAwait(false);

        return allOk;
    }

    public void ShowResult(BootstrapResult result)
    {
        if (result.Success)
        {
            _console.MarkupLine(
                $"[bold green]✓ Setup complete[/] — active profile: [bold]{result.ActiveProfile}[/]"
            );
        }
        else
        {
            _console.MarkupLine($"[bold red]✗ Setup failed:[/] {result.ErrorMessage}");
        }
    }

    private static string BuildChoiceBody(ProfileChoice choice)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[italic]\"{choice.Tagline}\"[/]");
        sb.AppendLine();
        foreach (var bullet in choice.Bullets)
        {
            sb.AppendLine($"  • {bullet}");
        }
        return sb.ToString().TrimEnd();
    }
}
