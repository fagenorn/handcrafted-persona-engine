namespace PersonaEngine.Lib.Bootstrapper.Manifest;

public sealed record InstallManifest(
    int SchemaVersion,
    string ManifestVersion,
    IReadOnlyList<AssetEntry> Assets
);
