using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Bootstrapper.Verification;

namespace PersonaEngine.Lib.Bootstrapper.Sources;

public sealed record DownloadProgress(long BytesDownloaded, long TotalBytes);

public sealed class AssetDownloader
{
    private readonly HttpClient _http;
    private readonly ILogger<AssetDownloader> _logger;

    public AssetDownloader(HttpClient http, ILogger<AssetDownloader> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task DownloadAsync(
        AssetDownload download,
        string destinationPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var partial = destinationPath + ".partial";
        long resumeFrom = File.Exists(partial) ? new FileInfo(partial).Length : 0;

        using var req = new HttpRequestMessage(HttpMethod.Get, download.Url);
        if (resumeFrom > 0)
            req.Headers.Range = new RangeHeaderValue(resumeFrom, null);

        using var resp = await _http
            .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (resp.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // Server says our partial is stale — start over.
            File.Delete(partial);
            resumeFrom = 0;
            using var retry = new HttpRequestMessage(HttpMethod.Get, download.Url);
            using var retryResp = await _http
                .SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            retryResp.EnsureSuccessStatusCode();
            await StreamToDiskAsync(retryResp, partial, destinationPath, download, progress, 0, ct)
                .ConfigureAwait(false);
            return;
        }

        resp.EnsureSuccessStatusCode();
        await StreamToDiskAsync(resp, partial, destinationPath, download, progress, resumeFrom, ct)
            .ConfigureAwait(false);
    }

    private async Task StreamToDiskAsync(
        HttpResponseMessage resp,
        string partial,
        string destinationPath,
        AssetDownload download,
        IProgress<DownloadProgress>? progress,
        long resumeFrom,
        CancellationToken ct
    )
    {
        // Open .partial for append (or create) so resume continues from the existing bytes.
        await using var fs = new FileStream(
            partial,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.None
        );
        fs.Seek(0, SeekOrigin.End);

        await using var net = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        long totalCopied = resumeFrom;
        var buffer = new byte[81920];
        int n;
        while (
            (n = await net.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false))
            > 0
        )
        {
            await fs.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            totalCopied += n;
            progress?.Report(new DownloadProgress(totalCopied, download.ExpectedSize));
        }
        await fs.FlushAsync(ct).ConfigureAwait(false);
        fs.Close();

        // Verify hash by streaming the full .partial through Sha256VerifyingStream.
        try
        {
            await using var verify = new Sha256VerifyingStream(
                File.OpenRead(partial),
                download.ExpectedSha256
            );
            if (download.PostProcess is not null)
            {
                await download.PostProcess(verify, ct).ConfigureAwait(false);
            }
            else
            {
                // Drain to end so the EOF check fires.
                await verify.CopyToAsync(Stream.Null, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            File.Delete(partial);
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
            throw;
        }

        if (download.PostProcess is null)
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
            File.Move(partial, destinationPath);
        }
        else
        {
            // PostProcess wrote the final files — discard the archive.
            File.Delete(partial);
        }
    }
}
