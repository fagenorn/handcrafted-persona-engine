using PersonaEngine.Lib.Bootstrapper.Manifest;
using PersonaEngine.Lib.Bootstrapper.Planner;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>UI implementation for --non-interactive mode. Picker call throws; progress runs silently.</summary>
public sealed class NoOpBootstrapUserInterface : IBootstrapUserInterface
{
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
            catch
            {
                allOk = false;
            }
        }
        return allOk;
    }

    public void ShowResult(BootstrapResult result) { }
}
