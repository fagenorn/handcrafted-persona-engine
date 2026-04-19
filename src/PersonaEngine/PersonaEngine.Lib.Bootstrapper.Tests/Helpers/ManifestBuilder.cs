using PersonaEngine.Lib.Bootstrapper.Manifest;

namespace PersonaEngine.Lib.Bootstrapper.Tests.Helpers;

internal static class ManifestBuilder
{
    public static AssetEntry Asset(
        string id,
        ProfileTier tier = ProfileTier.TryItOut,
        bool required = true,
        long size = 1000,
        string installPath = "Resources/x",
        AssetKind kind = AssetKind.Model,
        string sha = "sha-current",
        string[]? gates = null
    ) =>
        new(
            Id: id,
            Kind: kind,
            DisplayName: id,
            Capability: new AssetCapability("c", "d"),
            ProfileTier: tier,
            Required: required,
            Source: new HuggingFaceSource("repo", "v1", id + ".bin"),
            InstallPath: installPath,
            Sha256: sha,
            SizeBytes: size,
            Gates: gates ?? Array.Empty<string>()
        );

    public static InstallManifest Manifest(string version, params AssetEntry[] assets) =>
        new(SchemaVersion: 1, ManifestVersion: version, Assets: assets);

    public static InstallStateLock Lock(
        string manifestVersion,
        ProfileTier profile = ProfileTier.TryItOut,
        IDictionary<string, InstalledAssetRecord>? installed = null,
        IList<string>? exclusions = null
    ) =>
        new(
            SchemaVersion: 1,
            ManifestVersion: manifestVersion,
            InstalledAt: DateTimeOffset.UtcNow,
            SelectedProfile: profile,
            Assets: (IReadOnlyDictionary<string, InstalledAssetRecord>?)installed
                ?? new Dictionary<string, InstalledAssetRecord>(),
            UserExclusions: (IReadOnlyList<string>?)exclusions ?? Array.Empty<string>()
        );

    public static InstalledAssetRecord Record(string version = "v1", string sha = "sha-current") =>
        new(version, sha, DateTimeOffset.UtcNow);
}
