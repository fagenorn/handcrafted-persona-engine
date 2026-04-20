namespace PersonaEngine.Lib.Assets;

public interface IAssetCatalog
{
    AssetState GetAssetState(AssetId id);
    bool IsFeatureEnabled(FeatureId feature);
    IReadOnlyList<UserAsset> GetUserAssets(UserAssetType type);

    /// <summary>
    ///     Raised after the watcher debounce fires and the feature cache has been recomputed.
    ///     No payload: subscribers just re-query the catalog (user asset lists, feature state).
    ///     If a per-asset diff is ever needed, swap back to <c>EventHandler&lt;T&gt;</c>.
    /// </summary>
    event EventHandler Changed;
}
