using FluentAssertions;
using PersonaEngine.Lib.Assets.Manifest;
using Xunit;

namespace PersonaEngine.Lib.Tests.Assets;

public class ManifestValidationTests
{
    private static readonly InstallManifest Manifest = ManifestLoader.LoadEmbedded();

    [Fact]
    public void Every_install_path_is_relative_and_does_not_traverse()
    {
        foreach (var asset in Manifest.Assets)
        {
            Path.IsPathRooted(asset.InstallPath)
                .Should()
                .BeFalse($"asset '{asset.Id}' installPath '{asset.InstallPath}' must be relative");
            asset
                .InstallPath.Split('/', '\\')
                .Should()
                .NotContain(
                    "..",
                    $"asset '{asset.Id}' installPath '{asset.InstallPath}' must not contain '..'"
                );
        }
    }

    [Fact]
    public void Every_asset_id_is_unique()
    {
        var dupes = Manifest
            .Assets.GroupBy(a => a.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        dupes.Should().BeEmpty("asset ids must be unique");
    }

    [Fact]
    public void Every_install_path_is_unique()
    {
        var dupes = Manifest
            .Assets.GroupBy(a => a.InstallPath, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        dupes.Should().BeEmpty("two assets cannot install to the same path");
    }

    [Fact]
    public void Every_asset_has_sha256_or_is_NativeRuntime()
    {
        foreach (var asset in Manifest.Assets)
        {
            if (asset.Kind == AssetKind.NativeRuntime)
                continue; // sha lives on the NVIDIA archive
            asset
                .Sha256.Should()
                .NotBeNullOrWhiteSpace($"asset '{asset.Id}' is missing sha256");
            asset.Sha256.Length.Should().Be(64, $"asset '{asset.Id}' sha256 should be hex-64");
        }
    }

    [Fact]
    public void Every_HuggingFace_revision_is_a_pinned_tag_not_main()
    {
        foreach (var asset in Manifest.Assets)
        {
            if (asset.Source is HuggingFaceSource hf)
                hf.Revision.Should()
                    .NotBe(
                        "main",
                        $"asset '{asset.Id}' references HF 'main' branch — must use a pinned tag for reproducible installs"
                    );
        }
    }

    [Fact]
    public void Every_NvidiaRedist_platform_is_windows_x86_64()
    {
        foreach (var asset in Manifest.Assets)
        {
            if (asset.Source is NvidiaRedistSource nv)
                nv.Platform.Should()
                    .Be(
                        "windows-x86_64",
                        $"asset '{asset.Id}' targets unsupported NVIDIA platform '{nv.Platform}'"
                    );
        }
    }
}
