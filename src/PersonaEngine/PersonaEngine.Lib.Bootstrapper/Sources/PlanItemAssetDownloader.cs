using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Bootstrapper.Manifest;
using PersonaEngine.Lib.Bootstrapper.Planner;

namespace PersonaEngine.Lib.Bootstrapper.Sources;

/// <summary>
/// Bridges <see cref="IAssetDownloader"/> to the underlying <see cref="AssetDownloader"/> by
/// resolving the source URL via the correct source client and computing the destination path.
/// </summary>
public sealed class PlanItemAssetDownloader : IAssetDownloader
{
    private readonly AssetDownloader _inner;
    private readonly HuggingFaceClient _hf;
    private readonly NvidiaRedistClient _nv;
    private readonly string _resourceRoot;
    private readonly ILogger<PlanItemAssetDownloader>? _log;

    public PlanItemAssetDownloader(
        AssetDownloader inner,
        HuggingFaceClient hf,
        NvidiaRedistClient nv,
        string resourceRoot,
        ILogger<PlanItemAssetDownloader>? log = null
    )
    {
        _inner = inner;
        _hf = hf;
        _nv = nv;
        _resourceRoot = resourceRoot;
        _log = log;
    }

    public async Task DownloadAsync(
        AssetPlanItem item,
        IProgress<long> progress,
        CancellationToken ct
    )
    {
        IAssetSource client = item.Entry.Source.Type switch
        {
            SourceType.HuggingFace => _hf,
            SourceType.NvidiaRedist => _nv,
            _ => throw new InvalidOperationException(
                $"Unknown source type '{item.Entry.Source.Type}' for asset '{item.Entry.Id}'"
            ),
        };

        _log?.LogDebug(
            "Resolving download for asset '{AssetId}' via {Source}",
            item.Entry.Id,
            item.Entry.Source.Type
        );

        var download = await client.ResolveAsync(item.Entry, ct).ConfigureAwait(false);
        var destinationPath = Path.Combine(_resourceRoot, item.Entry.InstallPath);
        var wrappedProgress = new Progress<DownloadProgress>(p =>
            progress.Report(p.BytesDownloaded)
        );

        await _inner
            .DownloadAsync(download, destinationPath, wrappedProgress, ct)
            .ConfigureAwait(false);
    }
}
