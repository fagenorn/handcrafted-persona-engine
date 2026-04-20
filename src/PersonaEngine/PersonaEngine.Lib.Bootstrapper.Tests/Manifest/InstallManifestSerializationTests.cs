using System.Text.Json;
using FluentAssertions;
using PersonaEngine.Lib.Assets.Manifest;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests.Manifest;

public class InstallManifestSerializationTests
{
    [Fact]
    public void Roundtrips_a_full_manifest()
    {
        var manifest = new InstallManifest(
            SchemaVersion: 1,
            ManifestVersion: "2026.04.18",
            Assets: new[]
            {
                new AssetEntry(
                    Id: "tts.kokoro.model",
                    Kind: AssetKind.Model,
                    DisplayName: "Kokoro TTS Model",
                    Capability: new AssetCapability(
                        "Avatar voice (Kokoro)",
                        "Default speech synthesis engine"
                    ),
                    ProfileTier: ProfileTier.TryItOut,
                    Required: true,
                    Source: new HuggingFaceSource(
                        "fagenorn/persona-engine-assets",
                        "v2026.04.18",
                        "kokoro/model_slim.onnx"
                    ),
                    InstallPath: "Resources/Models/kokoro/model_slim.onnx",
                    Sha256: "abc123",
                    SizeBytes: 524288000,
                    Gates: new[] { "Tts.Kokoro" },
                    ExtractArchive: false
                ),
            }
        );

        var json = JsonSerializer.Serialize(manifest, ManifestLoader.JsonContext.InstallManifest);
        var deserialized = JsonSerializer.Deserialize(
            json,
            ManifestLoader.JsonContext.InstallManifest
        );

        deserialized.Should().BeEquivalentTo(manifest);
    }
}
