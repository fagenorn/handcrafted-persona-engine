namespace PersonaEngine.Lib.Bootstrapper.Manifest;

public sealed record InstalledAssetRecord(string Version, string Sha256, DateTimeOffset VerifiedAt);
