using System.Text.Json;
using FluentAssertions;
using PersonaEngine.Lib.Assets.Manifest;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests.Manifest;

/// <summary>
/// Verifies the STJ source-gen polymorphism on <see cref="AssetSource"/>: the
/// <c>type</c> discriminator is written/read by <see cref="ManifestJsonContext"/>
/// (see <c>[JsonPolymorphic]</c> / <c>[JsonDerivedType]</c> on the base record),
/// so there's no custom converter anymore — these tests pin the wire format.
/// </summary>
public class AssetSourceJsonConverterTests
{
    [Fact]
    public void Roundtrips_NvidiaRedistSource()
    {
        var src = new NvidiaRedistSource(
            Channel: "cuda",
            Package: "cuda_cudart",
            Version: "12.6.77",
            Platform: "windows-x86_64",
            ExtractFiles: new[] { "bin/cudart64_12.dll" }
        );

        var json = JsonSerializer.Serialize<AssetSource>(
            src,
            ManifestLoader.JsonContext.AssetSource
        );
        var deserialized = JsonSerializer.Deserialize(json, ManifestLoader.JsonContext.AssetSource);

        deserialized.Should().BeOfType<NvidiaRedistSource>().Which.Should().BeEquivalentTo(src);
        json.Should().Contain("\"type\": \"NvidiaRedist\"");
    }

    [Fact]
    public void Roundtrips_HuggingFaceSource()
    {
        var src = new HuggingFaceSource(
            Repo: "fagenorn/persona-engine-assets",
            Revision: "v2026.04.18",
            Path: "kokoro/model_slim.onnx"
        );

        var json = JsonSerializer.Serialize<AssetSource>(
            src,
            ManifestLoader.JsonContext.AssetSource
        );
        var deserialized = JsonSerializer.Deserialize(json, ManifestLoader.JsonContext.AssetSource);

        deserialized.Should().BeOfType<HuggingFaceSource>().Which.Should().BeEquivalentTo(src);
        json.Should().Contain("\"type\": \"HuggingFace\"");
    }

    [Fact]
    public void Throws_on_unknown_type()
    {
        var json = """{ "type": "Bogus", "url": "..." }""";

        Action act = () => JsonSerializer.Deserialize(json, ManifestLoader.JsonContext.AssetSource);

        act.Should().Throw<JsonException>();
    }
}
