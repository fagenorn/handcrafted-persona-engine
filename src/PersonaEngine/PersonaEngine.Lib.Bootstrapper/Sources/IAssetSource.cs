using PersonaEngine.Lib.Bootstrapper.Manifest;

namespace PersonaEngine.Lib.Bootstrapper.Sources;

public interface IAssetSource
{
    SourceType Type { get; }
    Task<AssetDownload> ResolveAsync(AssetEntry asset, CancellationToken ct);
}
