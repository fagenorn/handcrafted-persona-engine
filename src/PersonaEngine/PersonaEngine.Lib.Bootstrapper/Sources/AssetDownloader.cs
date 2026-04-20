using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Bootstrapper.Verification;

namespace PersonaEngine.Lib.Bootstrapper.Sources;

public sealed record DownloadProgress(long BytesDownloaded, long TotalBytes);

public sealed class AssetDownloader(HttpClient http, ILogger<AssetDownloader> logger)
{
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

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (resp.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // Server says our partial is stale — start over.
            File.Delete(partial);
            await RestartFromScratchAsync(download, partial, destinationPath, progress, ct)
                .ConfigureAwait(false);
            return;
        }

        // Sent a Range header but got 200 OK instead of 206 Partial Content. Some
        // CDNs / mirrors silently ignore Range and return the whole body from byte
        // zero. Appending that to an existing .partial would corrupt the file and
        // the SHA check would only catch it at the end — after we wasted bandwidth
        // on the overlap. Detect it up front and restart cleanly.
        if (resumeFrom > 0 && resp.StatusCode == System.Net.HttpStatusCode.OK)
        {
            logger.LogInformation(
                "Server ignored Range request for {Url}; restarting from scratch",
                download.Url
            );
            File.Delete(partial);
            await RestartFromScratchAsync(download, partial, destinationPath, progress, ct)
                .ConfigureAwait(false);
            return;
        }

        resp.EnsureSuccessStatusCode();
        await StreamToDiskAsync(resp, partial, destinationPath, download, progress, resumeFrom, ct)
            .ConfigureAwait(false);
    }

    private async Task RestartFromScratchAsync(
        AssetDownload download,
        string partial,
        string destinationPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct
    )
    {
        using var retry = new HttpRequestMessage(HttpMethod.Get, download.Url);
        using var retryResp = await http.SendAsync(
                retry,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            )
            .ConfigureAwait(false);
        retryResp.EnsureSuccessStatusCode();
        await StreamToDiskAsync(retryResp, partial, destinationPath, download, progress, 0, ct)
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
        // Phase 1: stream the HTTP body to .partial (resumable on retry).
        await using (
            var fs = new FileStream(
                partial,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.None
            )
        )
        {
            fs.Seek(0, SeekOrigin.End);

            await using var net = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

            long totalCopied = resumeFrom;
            var buffer = new byte[81920];
            int n;
            while (
                (
                    n = await net.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)
                        .ConfigureAwait(false)
                ) > 0
            )
            {
                await fs.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                totalCopied += n;
                progress?.Report(new DownloadProgress(totalCopied, download.ExpectedSize));
            }
            await fs.FlushAsync(ct).ConfigureAwait(false);
        }

        // Phase 2: verify SHA256 then (optionally) hand a SEEKABLE FileStream
        // to PostProcess. ZipArchive in Read mode requires CanSeek so it can
        // jump to the central directory at EOF; if it gets a non-seekable
        // stream it silently buffers everything into a MemoryStream, which
        // throws "Stream was too long" past Int32.MaxValue (~2 GB) — exactly
        // how qwen3-tts.zip (2.65 GB) crashed the BuildWithIt bootstrap.
        try
        {
            await using var verify = new FileStream(
                partial,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            );
            var actualHash = Convert
                .ToHexString(await SHA256.HashDataAsync(verify, ct).ConfigureAwait(false))
                .ToLowerInvariant();
            if (!actualHash.Equals(download.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new IntegrityException(
                    $"sha256 mismatch: expected {download.ExpectedSha256.ToLowerInvariant()}, got {actualHash}"
                );

            if (download.PostProcess is not null)
            {
                verify.Seek(0, SeekOrigin.Begin);
                await download.PostProcess(verify, ct).ConfigureAwait(false);
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
            // overwrite:true collapses the delete+move into one atomic-enough op and
            // avoids a window where the destination exists with zero bytes.
            File.Move(partial, destinationPath, overwrite: true);
        }
        else
        {
            // PostProcess wrote the final files — discard the archive.
            File.Delete(partial);
        }
    }
}
