using PersonaEngine.Lib.Assets.Manifest;

namespace PersonaEngine.Lib.Bootstrapper.Planner;

public sealed class AssetPlanner
{
    private readonly string _installRoot;

    public AssetPlanner(string installRoot)
    {
        _installRoot = installRoot;
    }

    public AssetPlan Compute(
        InstallManifest manifest,
        InstallStateLock lockState,
        ProfileTier selectedProfile,
        BootstrapMode mode
    )
    {
        var assetsById = manifest.Assets.ToDictionary(a => a.Id, StringComparer.Ordinal);
        var items = new List<AssetPlanItem>(manifest.Assets.Count);

        // Plan for everything in the manifest.
        foreach (var asset in manifest.Assets)
        {
            var inProfile = IsInProfile(asset.ProfileTier, selectedProfile);
            var excluded =
                mode == BootstrapMode.AutoIfMissing
                && lockState.UserExclusions.Contains(asset.Id, StringComparer.Ordinal);

            if (!inProfile || excluded)
            {
                items.Add(new AssetPlanItem(asset, AssetAction.Skip));
                continue;
            }

            var fileExists = AssetFileExists(asset);
            var lockEntry = lockState.Assets.GetValueOrDefault(asset.Id);

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

            items.Add(new AssetPlanItem(asset, action));
        }

        // Plan removals: assets in the lock but no longer in the selected profile.
        foreach (var (id, _) in lockState.Assets)
        {
            if (!assetsById.TryGetValue(id, out var asset))
                continue;
            if (IsInProfile(asset.ProfileTier, selectedProfile))
                continue;
            // already handled above as Skip; convert to Remove
            var idx = items.FindIndex(i => i.Entry.Id == id);
            if (idx >= 0)
                items[idx] = new AssetPlanItem(asset, AssetAction.Remove);
        }

        return new AssetPlan(items);
    }

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
