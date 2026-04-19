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
