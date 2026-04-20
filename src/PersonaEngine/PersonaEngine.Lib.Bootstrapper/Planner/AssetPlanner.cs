using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Assets.Manifest;

namespace PersonaEngine.Lib.Bootstrapper.Planner;

public sealed class AssetPlanner
{
    private readonly string _installRoot;
    private readonly ILogger<AssetPlanner>? _log;

    public AssetPlanner(string installRoot, ILogger<AssetPlanner>? log = null)
    {
        _installRoot = installRoot;
        _log = log;
    }

    public AssetPlan Compute(
        InstallManifest manifest,
        InstallStateLock lockState,
        ProfileTier selectedProfile,
        BootstrapMode mode
    )
    {
        var items = new List<AssetPlanItem>(manifest.Assets.Count);

        foreach (var asset in manifest.Assets)
        {
            items.Add(PlanFor(asset, lockState, selectedProfile, mode));
        }

        // Orphan reaper: anything in the lock that the manifest no longer knows about.
        // The previous implementation silently dropped these — a renamed asset would
        // linger on disk forever. Here we log it and skip the removal (we can't plan a
        // Remove without an AssetEntry). Operators see the warning and can clean up.
        var plannedIds = new HashSet<string>(
            manifest.Assets.Select(a => a.Id),
            StringComparer.Ordinal
        );
        foreach (var (id, _) in lockState.Assets)
        {
            if (!plannedIds.Contains(id))
            {
                _log?.LogWarning(
                    "Lock entry '{AssetId}' refers to an asset not present in manifest v{ManifestVersion}; leaving it alone (manual cleanup required if needed)",
                    id,
                    manifest.ManifestVersion
                );
            }
        }

        return new AssetPlan(items);
    }

    private AssetPlanItem PlanFor(
        AssetEntry asset,
        InstallStateLock lockState,
        ProfileTier selectedProfile,
        BootstrapMode mode
    )
    {
        var inProfile = IsInProfile(asset.ProfileTier, selectedProfile);
        var excluded = IsExcluded(asset, lockState, mode);
        var lockEntry = lockState.Assets.GetValueOrDefault(asset.Id);

        // Out-of-profile assets that are already locked need cleanup. Exclusions are
        // honored uniformly: a user-excluded asset that's already locked should also
        // be removed (previous behavior only applied exclusion on the install pass,
        // so toggling an exclusion after install left the file on disk).
        if (!inProfile || excluded)
        {
            return lockEntry is not null
                ? new AssetPlanItem(asset, AssetAction.Remove)
                : new AssetPlanItem(asset, AssetAction.Skip);
        }

        var fileExists = AssetFileExists(asset);
        var action = (mode, fileExists, lockEntry) switch
        {
            (BootstrapMode.Verify, true, not null) => AssetAction.Reverify,
            (BootstrapMode.Verify, _, _) => AssetAction.Download,
            (BootstrapMode.Repair, true, not null) when lockEntry.Sha256 == asset.Sha256 =>
                AssetAction.Reverify,
            (BootstrapMode.Repair, _, _) => AssetAction.Redownload,
            (_, true, not null) when lockEntry.Sha256 == asset.Sha256 => AssetAction.Skip,
            (_, true, not null) => AssetAction.Redownload,
            (_, false, not null) => AssetAction.Download,
            (_, _, null) => AssetAction.Download,
        };

        return new AssetPlanItem(asset, action);
    }

    private static bool IsExcluded(
        AssetEntry asset,
        InstallStateLock lockState,
        BootstrapMode mode
    ) =>
        // Exclusions only apply to "best-effort" runs. Verify/Repair/Reinstall are
        // explicit operator actions and should surface what the manifest says, so
        // we deliberately ignore exclusions there.
        mode == BootstrapMode.AutoIfMissing
        && lockState.UserExclusions.Contains(asset.Id, StringComparer.Ordinal);

    private bool AssetFileExists(AssetEntry asset)
    {
        var path = Path.IsPathRooted(asset.InstallPath)
            ? asset.InstallPath
            : Path.Combine(_installRoot, asset.InstallPath);
        return File.Exists(path)
            || (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any());
    }

    private static bool IsInProfile(ProfileTier assetTier, ProfileTier selected) =>
        (int)assetTier <= (int)selected;
}
