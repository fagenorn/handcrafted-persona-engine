using FluentAssertions;
using PersonaEngine.Lib.Assets.Manifest;
using PersonaEngine.Lib.Bootstrapper;
using PersonaEngine.Lib.Bootstrapper.Planner;
using PersonaEngine.Lib.Bootstrapper.Tests.Helpers;
using Xunit;
using static PersonaEngine.Lib.Bootstrapper.Tests.Helpers.ManifestBuilder;

// ManifestBuilder.Manifest(...) is used explicitly below because the
// Tests.Manifest namespace would otherwise shadow the static method import.

namespace PersonaEngine.Lib.Bootstrapper.Tests.Planner;

public class AssetPlannerTests : IDisposable
{
    private readonly string _root;

    public AssetPlannerTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "AssetPlannerTests-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void First_run_with_empty_lock_marks_all_in_profile_as_Download()
    {
        var manifest = ManifestBuilder.Manifest(
            "v1",
            Asset("a", ProfileTier.TryItOut),
            Asset("b", ProfileTier.StreamWithIt),
            Asset("c", ProfileTier.BuildWithIt)
        );
        var lockState = Lock("v1");

        var plan = new AssetPlanner(_root).Compute(
            manifest,
            lockState,
            ProfileTier.StreamWithIt,
            BootstrapMode.AutoIfMissing
        );

        plan.Items.Should().HaveCount(3);
        plan.Items.Single(i => i.Entry.Id == "a").Action.Should().Be(AssetAction.Download);
        plan.Items.Single(i => i.Entry.Id == "b").Action.Should().Be(AssetAction.Download);
        plan.Items.Single(i => i.Entry.Id == "c").Action.Should().Be(AssetAction.Skip);
    }

    [Fact]
    public void Skips_assets_present_with_matching_version_and_file_exists()
    {
        var asset = Asset("a", installPath: Path.Combine(_root, "a.bin"));
        File.WriteAllText(asset.InstallPath, "x");
        var manifest = ManifestBuilder.Manifest("v1", asset);
        var lockState = Lock(
            "v1",
            installed: new Dictionary<string, InstalledAssetRecord>
            {
                ["a"] = Record("v1", asset.Sha256),
            }
        );

        var plan = new AssetPlanner(_root).Compute(
            manifest,
            lockState,
            ProfileTier.TryItOut,
            BootstrapMode.AutoIfMissing
        );

        plan.Items.Single().Action.Should().Be(AssetAction.Skip);
    }

    [Fact]
    public void Marks_Redownload_when_lock_records_old_sha()
    {
        var asset = Asset("a", installPath: Path.Combine(_root, "a.bin"), sha: "new-sha");
        File.WriteAllText(asset.InstallPath, "x");
        var manifest = ManifestBuilder.Manifest("v2", asset);
        var lockState = Lock(
            "v1",
            installed: new Dictionary<string, InstalledAssetRecord>
            {
                ["a"] = Record("v1", "old-sha"),
            }
        );

        var plan = new AssetPlanner(_root).Compute(
            manifest,
            lockState,
            ProfileTier.TryItOut,
            BootstrapMode.AutoIfMissing
        );

        plan.Items.Single().Action.Should().Be(AssetAction.Redownload);
    }

    [Fact]
    public void Marks_Download_when_lock_says_installed_but_file_missing()
    {
        var asset = Asset("a", installPath: Path.Combine(_root, "a.bin"));
        var manifest = ManifestBuilder.Manifest("v1", asset);
        var lockState = Lock(
            "v1",
            installed: new Dictionary<string, InstalledAssetRecord>
            {
                ["a"] = Record("v1", asset.Sha256),
            }
        );

        var plan = new AssetPlanner(_root).Compute(
            manifest,
            lockState,
            ProfileTier.TryItOut,
            BootstrapMode.AutoIfMissing
        );

        plan.Items.Single().Action.Should().Be(AssetAction.Download);
    }

    [Fact]
    public void Honors_userExclusions_in_AutoIfMissing_mode()
    {
        var manifest = ManifestBuilder.Manifest(
            "v1",
            Asset("optional", required: false, installPath: Path.Combine(_root, "opt.bin"))
        );
        var lockState = Lock("v1", exclusions: new[] { "optional" });

        var plan = new AssetPlanner(_root).Compute(
            manifest,
            lockState,
            ProfileTier.TryItOut,
            BootstrapMode.AutoIfMissing
        );

        plan.Items.Single().Action.Should().Be(AssetAction.Skip);
    }

    [Fact]
    public void Reinstall_mode_ignores_userExclusions_for_picker_replay()
    {
        var manifest = ManifestBuilder.Manifest(
            "v1",
            Asset("optional", required: false, installPath: Path.Combine(_root, "opt.bin"))
        );
        var lockState = Lock("v1", exclusions: new[] { "optional" });

        var plan = new AssetPlanner(_root).Compute(
            manifest,
            lockState,
            ProfileTier.TryItOut,
            BootstrapMode.Reinstall
        );

        plan.Items.Single().Action.Should().Be(AssetAction.Download);
    }

    [Fact]
    public void Verify_mode_marks_installed_assets_as_Reverify()
    {
        var asset = Asset("a", installPath: Path.Combine(_root, "a.bin"));
        File.WriteAllText(asset.InstallPath, "x");
        var manifest = ManifestBuilder.Manifest("v1", asset);
        var lockState = Lock(
            "v1",
            installed: new Dictionary<string, InstalledAssetRecord>
            {
                ["a"] = Record("v1", asset.Sha256),
            }
        );

        var plan = new AssetPlanner(_root).Compute(
            manifest,
            lockState,
            ProfileTier.TryItOut,
            BootstrapMode.Verify
        );

        plan.Items.Single().Action.Should().Be(AssetAction.Reverify);
    }

    [Fact]
    public void Marks_Remove_when_installed_asset_no_longer_in_profile()
    {
        var manifest = ManifestBuilder.Manifest(
            "v1",
            Asset("kept", ProfileTier.TryItOut, installPath: Path.Combine(_root, "kept.bin")),
            Asset(
                "dropped",
                ProfileTier.BuildWithIt,
                installPath: Path.Combine(_root, "dropped.bin")
            )
        );
        File.WriteAllText(Path.Combine(_root, "dropped.bin"), "stale");

        var lockState = Lock(
            "v1",
            profile: ProfileTier.BuildWithIt,
            installed: new Dictionary<string, InstalledAssetRecord> { ["dropped"] = Record("v1") }
        );

        var plan = new AssetPlanner(_root).Compute(
            manifest,
            lockState,
            ProfileTier.TryItOut,
            BootstrapMode.Reinstall
        );

        plan.Items.Single(i => i.Entry.Id == "dropped").Action.Should().Be(AssetAction.Remove);
    }
}
