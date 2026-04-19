using PersonaEngine.Lib.Assets;
using PersonaEngine.Lib.Bootstrapper.Manifest;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>
///     Builds an <see cref="AssetCatalog" /> from a loaded <see cref="InstallManifest" />:
///     projects manifest entries into <see cref="AssetCatalogManifestEntry" />, derives the
///     per-<see cref="UserAssetType" /> roots from <see cref="AppContext.BaseDirectory" />,
///     and collects the shipped-default asset ids.
/// </summary>
internal static class AssetCatalogFactory
{
    public static AssetCatalog Build(InstallManifest manifest)
    {
        var resourcesRoot = Path.Combine(AppContext.BaseDirectory, "Resources");

        var entries = manifest
            .Assets.Select(a => new AssetCatalogManifestEntry(
                Id: new AssetId(a.Id),
                AbsoluteInstallPath: Path.Combine(resourcesRoot, a.InstallPath),
                Gates: a.Gates.Select(g => new FeatureId(g)).ToArray(),
                UserAssetCategory: a.UserAssetCategory
            ))
            .ToList();

        var roots = new Dictionary<UserAssetType, string>
        {
            [UserAssetType.Live2DModel] = Path.Combine(resourcesRoot, "live2d"),
            [UserAssetType.KokoroVoice] = Path.Combine(resourcesRoot, "kokoro", "voices"),
            [UserAssetType.RvcVoice] = Path.Combine(resourcesRoot, "rvc", "voices"),
        };

        // Shipped defaults are the user-content assets the engine installs out-of-the-box:
        // any required entry that targets a user-content category (Live2DModel, KokoroVoice,
        // RvcVoice). These IDs let AssetCatalog mark the corresponding directory entries as
        // IsDefault=true so the UI can distinguish bundled defaults from user-added content.
        var defaults = manifest
            .Assets.Where(a => a.UserAssetCategory.HasValue && a.Required)
            .Select(a => new AssetId(a.Id))
            .ToList();

        return new AssetCatalog(entries, roots, defaults);
    }
}
