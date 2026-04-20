using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Assets.Manifest;
using PersonaEngine.Lib.Bootstrapper.Planner;

namespace PersonaEngine.Lib.Bootstrapper.Sources;

/// <summary>
/// Bridges <see cref="IAssetDownloader"/> to the underlying <see cref="AssetDownloader"/> by
/// resolving the source URL via the correct source client and computing the destination path.
/// </summary>
public sealed class PlanItemAssetDownloader(
    AssetDownloader inner,
    HuggingFaceClient hf,
    NvidiaRedistClient nv,
    string resourceRoot,
    ILogger<PlanItemAssetDownloader>? log = null
) : IAssetDownloader
{
    public async Task DownloadAsync(
        AssetPlanItem item,
        IProgress<DownloadProgress> progress,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(progress);

        IAssetSource client = item.Entry.Source.Type switch
        {
            SourceType.HuggingFace => hf,
            SourceType.NvidiaRedist => nv,
            _ => throw new InvalidOperationException(
                $"Unknown source type '{item.Entry.Source.Type}' for asset '{item.Entry.Id}'"
            ),
        };

        log?.LogDebug(
            "Resolving download for asset '{AssetId}' via {Source}",
            item.Entry.Id,
            item.Entry.Source.Type
        );

        // Resolve the install path once against the resource root and pass it
        // to both the source client (for extraction targets) and the inner
        // AssetDownloader (for non-archive file moves). This is the single
        // source of truth for where the asset lands on disk.
        var destinationPath = Path.Combine(resourceRoot, item.Entry.InstallPath);
        var download = await client
            .ResolveAsync(item.Entry, destinationPath, ct)
            .ConfigureAwait(false);

        await inner.DownloadAsync(download, destinationPath, progress, ct).ConfigureAwait(false);
    }
}
