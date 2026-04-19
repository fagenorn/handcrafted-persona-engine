using System.Text.Json;
using FluentAssertions;
using PersonaEngine.Lib.Bootstrapper.Manifest;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests.Manifest;

public class AssetSourceJsonConverterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new AssetSourceJsonConverter() },
    };

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

        var json = JsonSerializer.Serialize<AssetSource>(src, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<AssetSource>(json, JsonOptions);

        deserialized.Should().BeOfType<NvidiaRedistSource>().Which.Should().BeEquivalentTo(src);
        json.Should().Contain("\"type\":\"NvidiaRedist\"");
    }

    [Fact]
    public void Roundtrips_HuggingFaceSource()
    {
        var src = new HuggingFaceSource(
            Repo: "fagenorn/persona-engine-assets",
            Revision: "v2026.04.18",
            Path: "kokoro/model_slim.onnx"
        );

        var json = JsonSerializer.Serialize<AssetSource>(src, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<AssetSource>(json, JsonOptions);

        deserialized.Should().BeOfType<HuggingFaceSource>().Which.Should().BeEquivalentTo(src);
        json.Should().Contain("\"type\":\"HuggingFace\"");
    }

    [Fact]
    public void Throws_on_unknown_type()
    {
        var json = """{ "type": "Bogus", "url": "..." }""";

        Action act = () => JsonSerializer.Deserialize<AssetSource>(json, JsonOptions);

        act.Should().Throw<JsonException>().WithMessage("*Bogus*");
    }
}
