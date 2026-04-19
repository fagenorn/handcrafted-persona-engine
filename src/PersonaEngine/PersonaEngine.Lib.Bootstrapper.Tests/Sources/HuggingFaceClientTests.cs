using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaEngine.Lib.Bootstrapper.Manifest;
using PersonaEngine.Lib.Bootstrapper.Sources;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests.Sources;

public class HuggingFaceClientTests
{
    [Fact]
    public async Task Resolve_builds_canonical_HF_resolve_url()
    {
        var client = new HuggingFaceClient(
            httpClient: new HttpClient(),
            endpoint: "https://huggingface.co",
            logger: NullLogger<HuggingFaceClient>.Instance
        );

        var entry = NewEntry(
            new HuggingFaceSource(
                Repo: "fagenorn/persona-engine-assets",
                Revision: "v2026.04.18",
                Path: "kokoro/model_slim.onnx"
            )
        );

        var result = await client.ResolveAsync(entry, CancellationToken.None);

        result
            .Url.AbsoluteUri.Should()
            .Be(
                "https://huggingface.co/fagenorn/persona-engine-assets/resolve/v2026.04.18/kokoro/model_slim.onnx"
            );
        result.ExpectedSha256.Should().Be("kokoro-sha");
        result.ExpectedSize.Should().Be(1024);
        result.PostProcess.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_honors_HF_ENDPOINT_override()
    {
        var client = new HuggingFaceClient(
            httpClient: new HttpClient(),
            endpoint: "https://hf.mirror.example.com",
            logger: NullLogger<HuggingFaceClient>.Instance
        );

        var entry = NewEntry(new HuggingFaceSource("repo", "tag", "file"));

        var result = await client.ResolveAsync(entry, CancellationToken.None);

        result.Url.AbsoluteUri.Should().StartWith("https://hf.mirror.example.com/");
    }

    [Fact]
    public async Task Resolve_rejects_main_branch_revisions()
    {
        var client = new HuggingFaceClient(
            new HttpClient(),
            "https://huggingface.co",
            NullLogger<HuggingFaceClient>.Instance
        );

        var entry = NewEntry(new HuggingFaceSource("repo", "main", "file"));

        Func<Task> act = () => client.ResolveAsync(entry, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*pinned*");
    }

    [Fact]
    public async Task Resolve_returns_extraction_post_process_when_extractArchive_is_true()
    {
        var client = new HuggingFaceClient(
            new HttpClient(),
            "https://huggingface.co",
            NullLogger<HuggingFaceClient>.Instance
        );

        var entry = NewEntry(
            new HuggingFaceSource("repo", "v1", "live2d/Aria.zip"),
            extractArchive: true,
            installPath: "Resources/UserContent/Live2D/Aria/"
        );

        var result = await client.ResolveAsync(entry, CancellationToken.None);

        result.PostProcess.Should().NotBeNull();
    }

    private static AssetEntry NewEntry(
        HuggingFaceSource src,
        bool extractArchive = false,
        string installPath = "Resources/Models/x"
    ) =>
        new(
            Id: "x",
            Kind: AssetKind.Model,
            DisplayName: "X",
            Capability: new AssetCapability("c", "d"),
            ProfileTier: ProfileTier.TryItOut,
            Required: false,
            Source: src,
            InstallPath: installPath,
            Sha256: "kokoro-sha",
            SizeBytes: 1024,
            Gates: Array.Empty<string>(),
            ExtractArchive: extractArchive
        );
}
