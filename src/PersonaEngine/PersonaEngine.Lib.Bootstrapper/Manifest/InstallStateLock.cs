namespace PersonaEngine.Lib.Bootstrapper.Manifest;

public sealed record InstallStateLock(
    int SchemaVersion,
    string ManifestVersion,
    DateTimeOffset InstalledAt,
    ProfileTier SelectedProfile,
    IReadOnlyDictionary<string, InstalledAssetRecord> Assets,
    IReadOnlyList<string> UserExclusions
)
{
    public static InstallStateLock Empty(string manifestVersion) =>
        new(
            SchemaVersion: 1,
            ManifestVersion: manifestVersion,
            InstalledAt: DateTimeOffset.UtcNow,
            SelectedProfile: ProfileTier.TryItOut,
            Assets: new Dictionary<string, InstalledAssetRecord>(),
            UserExclusions: Array.Empty<string>()
        );
}
