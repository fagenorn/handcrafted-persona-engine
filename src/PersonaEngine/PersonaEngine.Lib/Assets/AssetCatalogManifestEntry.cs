namespace PersonaEngine.Lib.Assets;

/// <summary>
///     Project-decoupled view of a manifest asset entry, used by IAssetCatalog.
///     Bootstrapper translates its rich AssetEntry into this when registering the catalog.
/// </summary>
public sealed record AssetCatalogManifestEntry(
    AssetId Id,
    string AbsoluteInstallPath,
    IReadOnlyList<FeatureId> Gates,
    UserAssetType? UserAssetCategory
);
