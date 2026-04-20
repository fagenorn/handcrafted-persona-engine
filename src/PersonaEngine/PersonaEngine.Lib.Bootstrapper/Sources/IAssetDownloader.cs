using PersonaEngine.Lib.Bootstrapper.Planner;

namespace PersonaEngine.Lib.Bootstrapper.Sources;

/// <summary>
/// Downloads a single planned asset item, reporting bytes-downloaded progress.
/// <para>
/// The progress callback delivers <see cref="DownloadProgress"/> so the UI can pick up
/// the real <c>TotalBytes</c> the moment the source resolves it. NVIDIA assets in
/// particular have <c>SizeBytes=0</c> in the static install manifest because their
/// real size lives in NVIDIA's runtime redist JSON — without the total in the
/// progress payload the bar would be stuck at 0%/0&nbsp;B.
/// </para>
/// </summary>
public interface IAssetDownloader
{
    Task DownloadAsync(
        AssetPlanItem item,
        IProgress<DownloadProgress> progress,
        CancellationToken ct
    );
}
