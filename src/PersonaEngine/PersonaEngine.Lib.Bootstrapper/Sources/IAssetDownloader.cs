using PersonaEngine.Lib.Bootstrapper.Planner;

namespace PersonaEngine.Lib.Bootstrapper.Sources;

/// <summary>
/// Downloads a single planned asset item, reporting bytes-downloaded progress.
/// </summary>
public interface IAssetDownloader
{
    Task DownloadAsync(AssetPlanItem item, IProgress<long> progress, CancellationToken ct);
}
