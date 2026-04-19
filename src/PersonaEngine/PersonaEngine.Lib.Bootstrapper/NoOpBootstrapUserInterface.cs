using PersonaEngine.Lib.Bootstrapper.Manifest;
using PersonaEngine.Lib.Bootstrapper.Planner;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>
/// <see cref="IBootstrapUserInterface"/> for <c>--non-interactive</c> mode.
/// Picker calls throw immediately; download progress runs silently.
/// </summary>
public sealed class NoOpBootstrapUserInterface : IBootstrapUserInterface
{
    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// Always thrown — the picker must not be called in non-interactive mode.
    /// Callers should supply a <see cref="BootstrapOptions.PreselectedProfile"/> instead.
    /// </exception>
    public Task<ProfileTier> PickProfileAsync(
        IReadOnlyList<ProfileChoice> choices,
        CancellationToken ct
    ) =>
        throw new InvalidOperationException(
            "Cannot prompt for a profile in non-interactive mode. Pass --profile <slug> to pre-select one."
        );

    /// <inheritdoc/>
    public void ShowPlanSummary(AssetPlan plan)
    {
        // Silent in non-interactive mode — no console output.
    }

    /// <inheritdoc/>
    public async Task<bool> RunWithProgressAsync(
        IReadOnlyList<AssetPlanItem> items,
        Func<AssetPlanItem, IProgress<long>, CancellationToken, Task> executeOne,
        CancellationToken ct
    )
    {
        var allOk = true;

        foreach (var item in items)
        {
            try
            {
                await executeOne(item, new Progress<long>(), ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                allOk = false;
            }
        }

        return allOk;
    }

    /// <inheritdoc/>
    public void ShowResult(BootstrapResult result)
    {
        // Silent in non-interactive mode — no console output.
    }
}
