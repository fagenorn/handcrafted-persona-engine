using FluentAssertions;
using PersonaEngine.Lib.Assets;
using Xunit;

namespace PersonaEngine.Lib.Tests.Assets;

public class AssetCatalogTests : IDisposable
{
    private readonly string _root;

    public AssetCatalogTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "AssetCatalogTests-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void GetAssetState_returns_Available_when_file_exists()
    {
        var path = Path.Combine(_root, "a.bin");
        File.WriteAllText(path, "x");
        var catalog = new AssetCatalog(
            new[]
            {
                new AssetCatalogManifestEntry(
                    new AssetId("a"),
                    path,
                    Array.Empty<FeatureId>(),
                    null
                ),
            },
            userContentRoots: new Dictionary<UserAssetType, string>(),
            shippedDefaults: Array.Empty<AssetId>()
        );

        catalog.GetAssetState(new AssetId("a")).Should().Be(AssetState.Available);
    }

    [Fact]
    public void GetAssetState_returns_Missing_when_file_absent()
    {
        var catalog = new AssetCatalog(
            new[]
            {
                new AssetCatalogManifestEntry(
                    new AssetId("a"),
                    Path.Combine(_root, "nope.bin"),
                    Array.Empty<FeatureId>(),
                    null
                ),
            },
            userContentRoots: new Dictionary<UserAssetType, string>(),
            shippedDefaults: Array.Empty<AssetId>()
        );

        catalog.GetAssetState(new AssetId("a")).Should().Be(AssetState.Missing);
    }

    [Fact]
    public void IsFeatureEnabled_AND_across_all_gating_assets()
    {
        var aPath = Path.Combine(_root, "a.bin");
        var bPath = Path.Combine(_root, "b.bin");
        File.WriteAllText(aPath, "x");
        // bPath intentionally missing

        var feature = new FeatureId("Tts.Qwen3");
        var catalog = new AssetCatalog(
            new[]
            {
                new AssetCatalogManifestEntry(new AssetId("a"), aPath, new[] { feature }, null),
                new AssetCatalogManifestEntry(new AssetId("b"), bPath, new[] { feature }, null),
            },
            userContentRoots: new Dictionary<UserAssetType, string>(),
            shippedDefaults: Array.Empty<AssetId>()
        );

        catalog
            .IsFeatureEnabled(feature)
            .Should()
            .BeFalse("feature requires both gating assets to be Available");
    }

    [Fact]
    public void IsFeatureEnabled_true_when_all_gating_assets_present()
    {
        var aPath = Path.Combine(_root, "a.bin");
        File.WriteAllText(aPath, "x");

        var feature = new FeatureId("Tts.Kokoro");
        var catalog = new AssetCatalog(
            new[]
            {
                new AssetCatalogManifestEntry(new AssetId("a"), aPath, new[] { feature }, null),
            },
            userContentRoots: new Dictionary<UserAssetType, string>(),
            shippedDefaults: Array.Empty<AssetId>()
        );

        catalog.IsFeatureEnabled(feature).Should().BeTrue();
    }

    [Fact]
    public void GetUserAssets_enumerates_directories_under_user_content_root()
    {
        var live2dRoot = Path.Combine(_root, "Live2D");
        Directory.CreateDirectory(Path.Combine(live2dRoot, "Aria"));
        Directory.CreateDirectory(Path.Combine(live2dRoot, "Bob"));

        var catalog = new AssetCatalog(
            Array.Empty<AssetCatalogManifestEntry>(),
            userContentRoots: new Dictionary<UserAssetType, string>
            {
                [UserAssetType.Live2DModel] = live2dRoot,
            },
            shippedDefaults: Array.Empty<AssetId>()
        );

        var items = catalog.GetUserAssets(UserAssetType.Live2DModel);

        items.Select(i => i.DisplayName).Should().BeEquivalentTo("Aria", "Bob");
        items.Should().AllSatisfy(i => i.IsDefault.Should().BeFalse());
    }

    [Fact]
    public void GetUserAssets_marks_shipped_defaults_with_IsDefault_true()
    {
        var live2dRoot = Path.Combine(_root, "Live2D");
        Directory.CreateDirectory(Path.Combine(live2dRoot, "Aria"));
        Directory.CreateDirectory(Path.Combine(live2dRoot, "Bob"));

        var ariaId = new AssetId("live2d.default.aria");
        var catalog = new AssetCatalog(
            new[]
            {
                new AssetCatalogManifestEntry(
                    ariaId,
                    Path.Combine(live2dRoot, "Aria"),
                    Array.Empty<FeatureId>(),
                    UserAssetType.Live2DModel
                ),
            },
            userContentRoots: new Dictionary<UserAssetType, string>
            {
                [UserAssetType.Live2DModel] = live2dRoot,
            },
            shippedDefaults: new[] { ariaId }
        );

        var aria = catalog
            .GetUserAssets(UserAssetType.Live2DModel)
            .Single(i => i.DisplayName == "Aria");
        aria.IsDefault.Should().BeTrue();

        var bob = catalog
            .GetUserAssets(UserAssetType.Live2DModel)
            .Single(i => i.DisplayName == "Bob");
        bob.IsDefault.Should().BeFalse();
    }
}
