namespace PersonaEngine.Lib.Assets.Manifest;

public sealed record InstallManifest(
    int SchemaVersion,
    string ManifestVersion,
    IReadOnlyList<AssetEntry> Assets
);
