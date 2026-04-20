using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Assets.Manifest;

namespace PersonaEngine.Lib.Bootstrapper.Sources;

#pragma warning disable CS9113 // Parameter is unread — logger kept for DI shape / future use.
public sealed class HuggingFaceClient(
    HttpClient httpClient,
    string endpoint,
    ILogger<HuggingFaceClient> logger
) : IAssetSource
#pragma warning restore CS9113
{
    public const string DefaultEndpoint = "https://huggingface.co";
    public const string EndpointEnvVar = "HF_ENDPOINT";

    // Normalise once so we don't duplicate the TrimEnd on every ResolveAsync call.
    private readonly string _endpoint = endpoint.TrimEnd('/');

    public SourceType Type => SourceType.HuggingFace;

    public static string ResolveEndpoint() =>
        Environment.GetEnvironmentVariable(EndpointEnvVar) ?? DefaultEndpoint;

    public Task<AssetDownload> ResolveAsync(
        AssetEntry asset,
        string resolvedInstallPath,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(asset);

        var src = (HuggingFaceSource)asset.Source;
        if (string.Equals(src.Revision, "main", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Asset '{asset.Id}' references HF revision 'main'. Asset revisions must be pinned to a tag for reproducible installs."
            );

        var url = new Uri($"{_endpoint}/{src.Repo}/resolve/{src.Revision}/{src.Path}");

        Func<Stream, CancellationToken, Task>? postProcess = null;
        if (asset.ExtractArchive)
        {
            // Use the caller-resolved absolute path (anchored at the resource
            // root) so extraction is independent of the current working
            // directory — see IAssetSource.ResolveAsync remarks.
            postProcess = (stream, c) =>
                ZipExtractor.ExtractAsync(stream, wantedEntries: null, resolvedInstallPath, c);
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
