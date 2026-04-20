using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaEngine.Lib.Bootstrapper.Sources;
using PersonaEngine.Lib.Bootstrapper.Verification;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests.Sources;

public class AssetDownloaderTests : IDisposable
{
    private readonly string _tempDir;

    public AssetDownloaderTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "AssetDownloaderTests-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Download_writes_file_atomically_and_verifies_hash()
    {
        var payload = Encoding.UTF8.GetBytes("hello asset");
        var sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var http = new HttpClient(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            })
        );
        var downloader = new AssetDownloader(http, NullLogger<AssetDownloader>.Instance);
        var dest = Path.Combine(_tempDir, "out.bin");

        await downloader.DownloadAsync(
            new AssetDownload(new Uri("https://x/y"), payload.Length, sha, PostProcess: null),
            dest,
            progress: null,
            CancellationToken.None
        );

        File.ReadAllBytes(dest).Should().Equal(payload);
        File.Exists(dest + ".partial").Should().BeFalse();
    }

    [Fact]
    public async Task Download_resumes_from_existing_partial_using_Range_header()
    {
        var payload = Encoding.UTF8.GetBytes("hello asset world");
        var sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        // Pre-write 5 bytes of partial.
        var dest = Path.Combine(_tempDir, "out.bin");
        File.WriteAllBytes(dest + ".partial", payload[..5]);

        var rangeHeaderSeen = (string?)null;
        var http = new HttpClient(
            new StubHandler(req =>
            {
                rangeHeaderSeen = req.Headers.Range?.ToString();
                // Return remaining bytes with 206 Partial Content
                var remaining = payload[5..];
                return new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(remaining),
                };
            })
        );
        var downloader = new AssetDownloader(http, NullLogger<AssetDownloader>.Instance);

        await downloader.DownloadAsync(
            new AssetDownload(new Uri("https://x/y"), payload.Length, sha, PostProcess: null),
            dest,
            progress: null,
            CancellationToken.None
        );

        rangeHeaderSeen.Should().Be("bytes=5-");
        File.ReadAllBytes(dest).Should().Equal(payload);
    }

    [Fact]
    public async Task Download_throws_IntegrityException_on_hash_mismatch()
    {
        var payload = Encoding.UTF8.GetBytes("payload");
        var http = new HttpClient(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            })
        );
        var downloader = new AssetDownloader(http, NullLogger<AssetDownloader>.Instance);
        var dest = Path.Combine(_tempDir, "out.bin");

        Func<Task> act = () =>
            downloader.DownloadAsync(
                new AssetDownload(
                    new Uri("https://x/y"),
                    payload.Length,
                    "0".PadLeft(64, '0'),
                    PostProcess: null
                ),
                dest,
                progress: null,
                CancellationToken.None
            );

        await act.Should().ThrowAsync<IntegrityException>();
        File.Exists(dest).Should().BeFalse("the partial should be deleted on hash mismatch");
        File.Exists(dest + ".partial").Should().BeFalse();
    }

    [Fact]
    public async Task Download_runs_PostProcess_when_provided()
    {
        var payload = Encoding.UTF8.GetBytes("zipdata");
        var sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var http = new HttpClient(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            })
        );
        var downloader = new AssetDownloader(http, NullLogger<AssetDownloader>.Instance);
        var dest = Path.Combine(_tempDir, "out.bin");

        var ranPostProcess = false;
        await downloader.DownloadAsync(
            new AssetDownload(
                new Uri("https://x/y"),
                payload.Length,
                sha,
                PostProcess: (s, _) =>
                {
                    ranPostProcess = true;
                    s.CopyTo(Stream.Null);
                    return Task.CompletedTask;
                }
            ),
            dest,
            progress: null,
            CancellationToken.None
        );

        ranPostProcess.Should().BeTrue();
    }

    [Fact]
    public async Task PostProcess_receives_seekable_stream_and_can_open_a_zip_archive()
    {
        // Regression: ZipArchive in Read mode requires a seekable stream so it
        // can fseek to the central directory at EOF. If the stream isn't
        // seekable (e.g. a hash-as-you-read wrapper), .NET silently buffers
        // the entire stream into a MemoryStream, which throws "Stream was too
        // long" past Int32.MaxValue (~2 GB). Real symptom: qwen3-tts.zip
        // (2.65 GB) crashed the bootstrapper with that message.
        var zipBytes = BuildSmallZip();
        var sha = Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();
        var http = new HttpClient(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes),
            })
        );
        var downloader = new AssetDownloader(http, NullLogger<AssetDownloader>.Instance);
        var dest = Path.Combine(_tempDir, "out.zip");

        var sawSeekableStream = false;
        var entryCount = 0;
        await downloader.DownloadAsync(
            new AssetDownload(
                new Uri("https://x/y"),
                zipBytes.Length,
                sha,
                PostProcess: (s, _) =>
                {
                    sawSeekableStream = s.CanSeek;
                    using var archive = new ZipArchive(s, ZipArchiveMode.Read, leaveOpen: false);
                    entryCount = archive.Entries.Count;
                    return Task.CompletedTask;
                }
            ),
            dest,
            progress: null,
            CancellationToken.None
        );

        sawSeekableStream
            .Should()
            .BeTrue(
                "ZipArchive needs a seekable stream — without it .NET buffers the whole "
                    + "archive into a MemoryStream and >2 GB archives throw 'Stream was too long'"
            );
        entryCount.Should().Be(1);
    }

    [Fact]
    public async Task PostProcess_is_not_run_when_hash_check_fails()
    {
        var payload = Encoding.UTF8.GetBytes("payload");
        var http = new HttpClient(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            })
        );
        var downloader = new AssetDownloader(http, NullLogger<AssetDownloader>.Instance);
        var dest = Path.Combine(_tempDir, "out.bin");

        var ranPostProcess = false;
        Func<Task> act = () =>
            downloader.DownloadAsync(
                new AssetDownload(
                    new Uri("https://x/y"),
                    payload.Length,
                    "0".PadLeft(64, '0'),
                    PostProcess: (_, _) =>
                    {
                        ranPostProcess = true;
                        return Task.CompletedTask;
                    }
                ),
                dest,
                progress: null,
                CancellationToken.None
            );

        await act.Should().ThrowAsync<IntegrityException>();
        ranPostProcess
            .Should()
            .BeFalse(
                "PostProcess must not run on corrupt bytes — that's the whole point of verifying first"
            );
        File.Exists(dest + ".partial").Should().BeFalse();
    }

    private static byte[] BuildSmallZip()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("hello.txt");
            using var w = new StreamWriter(entry.Open());
            w.Write("hi");
        }
        return ms.ToArray();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => Task.FromResult(_respond(request));
    }
}
