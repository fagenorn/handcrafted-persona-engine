using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Assets.Manifest;
using PersonaEngine.Lib.Bootstrapper.Planner;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>UI implementation for --non-interactive mode. Picker call throws; progress runs silently.</summary>
public sealed class NoOpBootstrapUserInterface : IBootstrapUserInterface
{
    private readonly ILogger<NoOpBootstrapUserInterface>? _log;

    public NoOpBootstrapUserInterface(ILogger<NoOpBootstrapUserInterface>? log = null)
    {
        _log = log;
    }

    public Task<ProfileTier> PickProfileAsync(
        IReadOnlyList<ProfileChoice> choices,
        CancellationToken ct
    ) =>
        throw new InvalidOperationException(
            "Cannot prompt for profile in --non-interactive mode. Pass --profile=<try|stream|build>."
        );

    public void ShowPlanSummary(AssetPlan plan) { }

    public async Task<bool> RunWithProgressAsync(
        IReadOnlyList<AssetPlanItem> items,
        Func<AssetPlanItem, IProgress<long>, CancellationToken, Task> executeOne,
        CancellationToken ct
    )
    {
        var allOk = true;
        var noopProgress = new Progress<long>(_ => { });
        foreach (var item in items)
        {
            try
            {
                await executeOne(item, noopProgress, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                allOk = false;
                // Non-interactive mode swallows the throw to keep going through
                // the rest of the plan, but operators still need to see what
                // failed. Log full exception detail at error level.
                _log?.LogError(
                    ex,
                    "Bootstrap asset '{AssetId}' failed in non-interactive mode",
                    item.Entry.Id
                );
            }
        }
        return allOk;
    }

    public void ShowResult(BootstrapResult result) { }
}
