namespace PersonaEngine.Lib.Assets;

public sealed class AssetCatalogChangedEventArgs : EventArgs
{
    public IReadOnlyList<AssetId> ChangedAssets { get; }

    public AssetCatalogChangedEventArgs(IReadOnlyList<AssetId> changedAssets) =>
        ChangedAssets = changedAssets;
}
