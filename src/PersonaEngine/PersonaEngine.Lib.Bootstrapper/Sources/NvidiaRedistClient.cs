using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Assets.Manifest;

namespace PersonaEngine.Lib.Bootstrapper.Sources;

public sealed class NvidiaRedistClient : IAssetSource
{
    public const string CudaArchiveBaseUrl =
        "https://developer.download.nvidia.com/compute/cuda/redist/";
    public const string CudnnArchiveBaseUrl =
        "https://developer.download.nvidia.com/compute/cudnn/redist/";

    private readonly HttpClient _http;
    private readonly ILogger<NvidiaRedistClient> _logger;
    private readonly ConcurrentDictionary<string, Task<NvidiaRedistManifest>> _manifestCache =
        new();

    public SourceType Type => SourceType.NvidiaRedist;

    public NvidiaRedistClient(HttpClient http, ILogger<NvidiaRedistClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<AssetDownload> ResolveAsync(
        AssetEntry asset,
        string resolvedInstallPath,
        CancellationToken ct
    )
    {
        var src = (NvidiaRedistSource)asset.Source;
        var (manifestUrl, baseUrl) = src.Channel switch
        {
            "cuda" => ($"{CudaArchiveBaseUrl}redistrib_{src.Version}.json", CudaArchiveBaseUrl),
            "cudnn" => ($"{CudnnArchiveBaseUrl}redistrib_{src.Version}.json", CudnnArchiveBaseUrl),
            _ => throw new InvalidOperationException(
                $"Unknown NVIDIA redist channel '{src.Channel}'"
            ),
        };

        var manifest = await _manifestCache
            .GetOrAdd(manifestUrl, url => FetchManifestAsync(url, ct))
            .ConfigureAwait(false);

        if (!manifest.Packages.TryGetValue(src.Package, out var pkg))
            throw new InvalidOperationException(
                $"Package '{src.Package}' not found in NVIDIA manifest at {manifestUrl}"
            );
        if (!pkg.Platforms.TryGetValue(src.Platform, out var platform))
            throw new KeyNotFoundException(
                $"Platform '{src.Platform}' not found for package '{src.Package}' in {manifestUrl}"
            );

        var url = new Uri(new Uri(baseUrl), platform.RelativePath);
        var extractFiles = src.ExtractFiles;

        return new AssetDownload(
            Url: url,
            ExpectedSize: platform.Size,
            ExpectedSha256: platform.Sha256,
            // Use the caller-resolved absolute path (anchored at the resource
            // root) so extraction is independent of the current working
            // directory — see IAssetSource.ResolveAsync remarks.
            PostProcess: (stream, c) =>
                ZipExtractor.ExtractAsync(stream, extractFiles, resolvedInstallPath, c)
        );
    }

    private async Task<NvidiaRedistManifest> FetchManifestAsync(string url, CancellationToken ct)
    {
        _logger.LogDebug("Fetching NVIDIA redist manifest {Url}", url);
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return NvidiaRedistManifest.Parse(json);
    }
}
