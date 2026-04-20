using FluentAssertions;
using PersonaEngine.Lib.Assets.Manifest;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests.Manifest;

public class ManifestLoaderTests
{
    [Fact]
    public void Load_from_string_parses_valid_manifest()
    {
        var json = """
            { "schemaVersion": 1, "manifestVersion": "test", "assets": [] }
            """;

        var manifest = ManifestLoader.LoadFromJson(json);

        manifest.SchemaVersion.Should().Be(1);
        manifest.ManifestVersion.Should().Be("test");
        manifest.Assets.Should().BeEmpty();
    }

    [Fact]
    public void Load_throws_on_unknown_schema_version()
    {
        var json = """
            { "schemaVersion": 999, "manifestVersion": "x", "assets": [] }
            """;

        Action act = () => ManifestLoader.LoadFromJson(json);

        act.Should().Throw<NotSupportedException>().WithMessage("*999*");
    }

    [Fact]
    public void Load_throws_on_malformed_json()
    {
        Action act = () => ManifestLoader.LoadFromJson("{ not json");

        act.Should().Throw<System.Text.Json.JsonException>();
    }

    [Fact]
    public void LoadEmbedded_returns_the_shipped_manifest()
    {
        var manifest = ManifestLoader.LoadEmbedded();

        manifest.Should().NotBeNull();
        manifest.SchemaVersion.Should().Be(1);
    }
}
