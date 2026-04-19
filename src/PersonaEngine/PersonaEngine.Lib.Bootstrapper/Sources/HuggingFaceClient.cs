using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Assets.Manifest;

namespace PersonaEngine.Lib.Bootstrapper.Sources;

public sealed class HuggingFaceClient : IAssetSource
{
    public const string DefaultEndpoint = "https://huggingface.co";
    public const string EndpointEnvVar = "HF_ENDPOINT";

    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly ILogger<HuggingFaceClient> _logger;

    public SourceType Type => SourceType.HuggingFace;

    public HuggingFaceClient(
        HttpClient httpClient,
        string endpoint,
        ILogger<HuggingFaceClient> logger
    )
    {
        _http = httpClient;
        _endpoint = endpoint.TrimEnd('/');
        _logger = logger;
    }

    public static string ResolveEndpoint() =>
        Environment.GetEnvironmentVariable(EndpointEnvVar) ?? DefaultEndpoint;

    public Task<AssetDownload> ResolveAsync(AssetEntry asset, CancellationToken ct)
    {
        var src = (HuggingFaceSource)asset.Source;
        if (string.Equals(src.Revision, "main", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Asset '{asset.Id}' references HF revision 'main'. Asset revisions must be pinned to a tag for reproducible installs."
            );

        var url = new Uri($"{_endpoint}/{src.Repo}/resolve/{src.Revision}/{src.Path}");

        Func<Stream, CancellationToken, Task>? postProcess = null;
        if (asset.ExtractArchive)
        {
            var installPath = asset.InstallPath;
            postProcess = (stream, c) =>
                ZipExtractor.ExtractAsync(stream, wantedEntries: null, installPath, c);
        }

        return Task.FromResult(
            new AssetDownload(
                Url: url,
                ExpectedSize: asset.SizeBytes,
                ExpectedSha256: asset.Sha256,
                PostProcess: postProcess
            )
        );
    }
}
