using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaEngine.Lib.Bootstrapper.Manifest;
using PersonaEngine.Lib.Bootstrapper.Sources;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests.Sources;

public class NvidiaRedistClientTests
{
    [Fact]
    public async Task Resolve_builds_correct_url_and_sha_from_manifest()
    {
        var manifestJson = File.ReadAllText(Path.Combine("TestData", "redistrib_12.6.77.json"));
        var http = new HttpClient(
            new StubHandler(req =>
            {
                req.RequestUri!.AbsoluteUri.Should().EndWith("/cuda/redist/redistrib_12.6.77.json");
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(manifestJson),
                };
            })
        );
        var client = new NvidiaRedistClient(http, NullLogger<NvidiaRedistClient>.Instance);

        var entry = NewEntry(
            new NvidiaRedistSource(
                Channel: "cuda",
                Package: "cuda_cudart",
                Version: "12.6.77",
                Platform: "windows-x86_64",
                ExtractFiles: new[] { "bin/cudart64_12.dll" }
            )
        );

        var result = await client.ResolveAsync(entry, CancellationToken.None);

        result
            .Url.AbsoluteUri.Should()
            .Be(
                "https://developer.download.nvidia.com/compute/cuda/redist/cuda_cudart/windows-x86_64/cuda_cudart-windows-x86_64-12.6.77-archive.zip"
            );
        result.ExpectedSha256.Should().HaveLength(64);
        result.ExpectedSize.Should().Be(5612345);
        result.PostProcess.Should().NotBeNull();
    }

    [Fact]
    public async Task Resolve_throws_for_unknown_channel()
    {
        var http = new HttpClient(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            })
        );
        var client = new NvidiaRedistClient(http, NullLogger<NvidiaRedistClient>.Instance);

        var entry = NewEntry(
            new NvidiaRedistSource(
                Channel: "bogus",
                Package: "x",
                Version: "1",
                Platform: "windows-x86_64",
                ExtractFiles: Array.Empty<string>()
            )
        );

        Func<Task> act = () => client.ResolveAsync(entry, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*bogus*");
    }

    [Fact]
    public async Task Resolve_caches_manifest_per_instance()
    {
        var fetchCount = 0;
        var manifestJson = File.ReadAllText(Path.Combine("TestData", "redistrib_12.6.77.json"));
        var http = new HttpClient(
            new StubHandler(_ =>
            {
                fetchCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(manifestJson),
                };
            })
        );
        var client = new NvidiaRedistClient(http, NullLogger<NvidiaRedistClient>.Instance);

        var entry = NewEntry(
            new NvidiaRedistSource(
                "cuda",
                "cuda_cudart",
                "12.6.77",
                "windows-x86_64",
                new[] { "bin/cudart64_12.dll" }
            )
        );

        await client.ResolveAsync(entry, CancellationToken.None);
        await client.ResolveAsync(entry, CancellationToken.None);

        fetchCount.Should().Be(1);
    }

    private static AssetEntry NewEntry(NvidiaRedistSource source) =>
        new(
            Id: "x",
            Kind: AssetKind.NativeRuntime,
            DisplayName: "X",
            Capability: new AssetCapability("c", "d"),
            ProfileTier: ProfileTier.TryItOut,
            Required: true,
            Source: source,
            InstallPath: "runtimes/win-x64/native/",
            Sha256: "ignored",
            SizeBytes: 0,
            Gates: Array.Empty<string>()
        );

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
