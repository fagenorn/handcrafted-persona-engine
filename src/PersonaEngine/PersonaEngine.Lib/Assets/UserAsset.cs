namespace PersonaEngine.Lib.Assets;

public sealed record UserAsset(
    UserAssetType Type,
    string DisplayName,
    string AbsolutePath,
    bool IsDefault
);
