namespace PersonaEngine.Lib.Assets;

public interface IAssetCatalog
{
    AssetState GetAssetState(AssetId id);
    bool IsFeatureEnabled(FeatureId feature);
    IReadOnlyList<UserAsset> GetUserAssets(UserAssetType type);
    event EventHandler<AssetCatalogChangedEventArgs> Changed;
}
