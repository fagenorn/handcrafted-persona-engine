using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Assets.Manifest;
using PersonaEngine.Lib.Bootstrapper.GpuPreflight;
using PersonaEngine.Lib.Bootstrapper.Planner;
using PersonaEngine.Lib.Bootstrapper.Sources;

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

    /// <summary>
    /// Non-interactive mode can't prompt, so any GPU preflight failure aborts
    /// the run. Operators can pass <c>--skip-gpu-check</c> on the CLI to run
    /// anyway (useful in WSL / CI where nvidia-smi may report oddly).
    /// </summary>
    public Task<GpuWarningResponse> ShowGpuWarningAsync(GpuStatus status, CancellationToken ct)
    {
        _log?.LogError(
            "GPU preflight failed in non-interactive mode ({Kind}): {Detail}",
            status.Kind,
            status.Detail
        );
        return Task.FromResult(GpuWarningResponse.Abort);
    }

    public void ShowPlanSummary(AssetPlan plan) { }

    public async Task<bool> RunWithProgressAsync(
        IReadOnlyList<AssetPlanItem> items,
        Func<AssetPlanItem, IProgress<DownloadProgress>, CancellationToken, Task> executeOne,
        CancellationToken ct
    )
    {
        var allOk = true;
        var noopProgress = new Progress<DownloadProgress>(_ => { });
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
