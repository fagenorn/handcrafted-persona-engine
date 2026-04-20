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
    public void Picks_cuda12_variant_when_platform_nests_by_cuda_variant()
    {
        // cuDNN's redistrib JSON nests platform entries by cuda_variant instead
        // of putting relative_path directly under the platform key.
        var json = """
            {
              "cudnn": {
                "name": "cuDNN", "license": "x", "version": "9.1.1",
                "windows-x86_64": {
                  "cuda11": {
                    "relative_path": "cuda11/path.zip",
                    "sha256": "1111111111111111111111111111111111111111111111111111111111111111",
                    "size": "100"
                  },
                  "cuda12": {
                    "relative_path": "cuda12/path.zip",
                    "sha256": "2222222222222222222222222222222222222222222222222222222222222222",
                    "size": "200"
                  }
                }
              }
            }
            """;

        var manifest = NvidiaRedistManifest.Parse(json);

        var win = manifest.Packages["cudnn"].Platforms["windows-x86_64"];
        win.RelativePath.Should().Be("cuda12/path.zip");
        win.Sha256.Should().StartWith("2222");
        win.Size.Should().Be(200);
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
