using PersonaEngine.Lib.Bootstrapper.Manifest;
using PersonaEngine.Lib.Bootstrapper.Planner;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>
/// The profile the user chose together with optional per-asset exclusions they ticked off.
/// </summary>
public sealed record ProfileChoice(ProfileTier Tier, IReadOnlyList<string> ExcludedAssetIds);

/// <summary>
/// Abstraction over the installer UI (CLI wizard, GUI, or headless auto-select).
/// All methods are called by <see cref="BootstrapRunner"/> on the host's thread.
/// </summary>
public interface IBootstrapUserInterface
{
    /// <summary>
    /// Ask the user to pick a profile tier.  Returns null if the user cancels.
    /// </summary>
    Task<ProfileChoice?> PickProfileAsync(CancellationToken ct);

    /// <summary>
    /// Show the computed download plan and ask for confirmation.
    /// Return <c>true</c> to proceed, <c>false</c> to abort.
    /// </summary>
    Task<bool> ConfirmPlanAsync(AssetPlan plan, CancellationToken ct);

    /// <summary>Report per-asset download progress (called on each progress tick).</summary>
    void ReportProgress(AssetPlanItem item, long bytesDownloaded, long totalBytes);

    /// <summary>Show the final outcome to the user.</summary>
    Task ShowResultAsync(BootstrapResult result, CancellationToken ct);
}
