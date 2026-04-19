using FluentAssertions;
using PersonaEngine.Lib.Bootstrapper.Sources;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests.Sources;

public class NvidiaRedistManifestTests
{
    [Fact]
    public void Parses_full_redistrib_json_fixture()
    {
        var json = File.ReadAllText(Path.Combine("TestData", "redistrib_12.6.77.json"));

        var manifest = NvidiaRedistManifest.Parse(json);

        manifest.Packages.Should().ContainKey("cuda_cudart");
        var cudart = manifest.Packages["cuda_cudart"];
        cudart.Version.Should().Be("12.6.77");
        cudart.Platforms.Should().ContainKey("windows-x86_64");
        cudart.Platforms["windows-x86_64"].Sha256.Should().HaveLength(64);
        cudart.Platforms["windows-x86_64"].Size.Should().Be(5612345);
    }

    [Fact]
    public void Parses_size_when_serialized_as_number()
    {
        var json = """
            {
              "cuda_x": {
                "name": "x", "license": "x", "version": "1",
                "windows-x86_64": {
                  "relative_path": "p", "sha256": "0000000000000000000000000000000000000000000000000000000000000000",
                  "md5": "00000000000000000000000000000000", "size": 4242
                }
              }
            }
            """;

        var manifest = NvidiaRedistManifest.Parse(json);
        manifest.Packages["cuda_x"].Platforms["windows-x86_64"].Size.Should().Be(4242);
    }
}
