namespace PersonaEngine.Lib.Assets.Manifest;

public sealed record AssetEntry(
    string Id,
    AssetKind Kind,
    string DisplayName,
    AssetCapability Capability,
    ProfileTier ProfileTier,
    bool Required,
    AssetSource Source,
    string InstallPath,
    string Sha256,
    long SizeBytes,
    IReadOnlyList<string> Gates,
    bool ExtractArchive = false,
    UserAssetType? UserAssetCategory = null
);
