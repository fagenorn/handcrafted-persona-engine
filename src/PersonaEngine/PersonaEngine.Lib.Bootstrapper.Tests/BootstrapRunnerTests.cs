using FluentAssertions;
using NSubstitute;
using PersonaEngine.Lib.Assets;
using PersonaEngine.Lib.Assets.Manifest;
using PersonaEngine.Lib.Bootstrapper.GpuPreflight;
using PersonaEngine.Lib.Bootstrapper.Planner;
using PersonaEngine.Lib.Bootstrapper.Sources;
using PersonaEngine.Lib.Bootstrapper.Tests.Helpers;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests;

public sealed class BootstrapRunnerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _lockPath;
    private readonly IAssetDownloader _downloader;
    private readonly IAssetCatalog _catalog;
    private readonly IBootstrapUserInterface _ui;
    private readonly IGpuPreflightCheck _gpuPreflight;

    public BootstrapRunnerTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "BootstrapRunnerTests-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_tempDir);
        _lockPath = Path.Combine(_tempDir, "install-state.lock.json");

        _downloader = Substitute.For<IAssetDownloader>();
        _catalog = Substitute.For<IAssetCatalog>();
        _ui = Substitute.For<IBootstrapUserInterface>();
        _gpuPreflight = Substitute.For<IGpuPreflightCheck>();

        // Default: preflight passes. Individual tests that exercise failure
        // paths override this via .InspectAsync(...).Returns(...).
        _gpuPreflight
            .InspectAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(GpuStatus.Pass("580.65", "Test GPU", (8, 9))));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static AssetEntry BuildAsset(string id, ProfileTier tier = ProfileTier.TryItOut) =>
        ManifestBuilder.Asset(id, tier, installPath: id + ".bin");

    private static InstallManifest BuildManifest(params AssetEntry[] assets) =>
        ManifestBuilder.Manifest("v1", assets);

    private BootstrapRunner BuildRunner(InstallManifest manifest)
    {
        var lockStore = new InstallStateLockStore(_lockPath);
        var planner = new AssetPlanner(_tempDir);
        return new BootstrapRunner(
            manifest,
            lockStore,
            planner,
            _downloader,
            _catalog,
            _ui,
            _gpuPreflight,
            _tempDir
        );
    }

    [Fact]
    public async Task AutoIfMissing_with_no_lock_runs_picker_and_downloads()
    {
        var asset = BuildAsset("model-a");
        var manifest = BuildManifest(asset);

        _ui.PickProfileAsync(Arg.Any<IReadOnlyList<ProfileChoice>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ProfileTier.TryItOut));
        _ui.RunWithProgressAsync(
                Arg.Any<IReadOnlyList<AssetPlanItem>>(),
                Arg.Any<
                    Func<AssetPlanItem, IProgress<DownloadProgress>, CancellationToken, Task>
                >(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(true));

        var runner = BuildRunner(manifest);
        var result = await runner.RunAsync(
            new BootstrapOptions { Mode = BootstrapMode.AutoIfMissing },
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.ChangesApplied.Should().BeTrue();
        result.ActiveProfile.Should().Be(ProfileTier.TryItOut);
        File.Exists(_lockPath)
            .Should()
            .BeTrue("lock file should be written after a successful run");

        // Regression: lock must record the downloaded asset and the chosen profile.
        var writtenLock = new InstallStateLockStore(_lockPath).Read("v1");
        writtenLock.SelectedProfile.Should().Be(ProfileTier.TryItOut);
        writtenLock.Assets.Should().ContainKey("model-a");
    }

    [Fact]
    public async Task AutoIfMissing_with_complete_lock_skips_picker_and_downloads()
    {
        var asset = BuildAsset("model-b");
        var manifest = BuildManifest(asset);

        // Write a lock that says model-b is already installed with the current sha.
        var lockStore = new InstallStateLockStore(_lockPath);
        var existing = new InstallStateLock(
            SchemaVersion: 1,
            ManifestVersion: "v1",
            InstalledAt: DateTimeOffset.UtcNow,
            SelectedProfile: ProfileTier.TryItOut,
            Assets: new Dictionary<string, InstalledAssetRecord>
            {
                ["model-b"] = new InstalledAssetRecord("v1", "sha-current", DateTimeOffset.UtcNow),
            },
            UserExclusions: Array.Empty<string>()
        );
        lockStore.Write(existing);

        // Create the file on disk so AssetPlanner considers it present.
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "model-b.bin"), "dummy");

        var planner = new AssetPlanner(_tempDir);
        var runner = new BootstrapRunner(
            manifest,
            lockStore,
            planner,
            _downloader,
            _catalog,
            _ui,
            _gpuPreflight,
            _tempDir
        );

        var result = await runner.RunAsync(
            new BootstrapOptions { Mode = BootstrapMode.AutoIfMissing },
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.ChangesApplied.Should().BeFalse();
        result.ActiveProfile.Should().Be(ProfileTier.TryItOut);
        await _ui.DidNotReceive()
            .PickProfileAsync(
                Arg.Any<IReadOnlyList<ProfileChoice>>(),
                Arg.Any<CancellationToken>()
            );
        await _downloader
            .DidNotReceive()
            .DownloadAsync(
                Arg.Any<AssetPlanItem>(),
                Arg.Any<IProgress<DownloadProgress>>(),
                Arg.Any<CancellationToken>()
            );
        await _ui.DidNotReceive()
            .RunWithProgressAsync(
                Arg.Any<IReadOnlyList<AssetPlanItem>>(),
                Arg.Any<
                    Func<AssetPlanItem, IProgress<DownloadProgress>, CancellationToken, Task>
                >(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Offline_mode_with_missing_assets_returns_failure_without_network()
    {
        var asset = BuildAsset("model-c");
        var manifest = BuildManifest(asset);

        // No lock file, no files on disk — assets are missing.
        _ui.PickProfileAsync(Arg.Any<IReadOnlyList<ProfileChoice>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ProfileTier.TryItOut));

        var runner = BuildRunner(manifest);
        var result = await runner.RunAsync(
            new BootstrapOptions
            {
                Mode = BootstrapMode.Offline,
                PreselectedProfile = ProfileTier.TryItOut,
            },
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Offline mode");
        await _downloader
            .DidNotReceive()
            .DownloadAsync(
                Arg.Any<AssetPlanItem>(),
                Arg.Any<IProgress<DownloadProgress>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PreselectedProfile_overrides_picker()
    {
        var asset = BuildAsset("model-d", ProfileTier.BuildWithIt);
        var manifest = BuildManifest(asset);

        _ui.RunWithProgressAsync(
                Arg.Any<IReadOnlyList<AssetPlanItem>>(),
                Arg.Any<
                    Func<AssetPlanItem, IProgress<DownloadProgress>, CancellationToken, Task>
                >(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(true));

        var runner = BuildRunner(manifest);
        var result = await runner.RunAsync(
            new BootstrapOptions
            {
                Mode = BootstrapMode.AutoIfMissing,
                PreselectedProfile = ProfileTier.BuildWithIt,
            },
            CancellationToken.None
        );

        result.ActiveProfile.Should().Be(ProfileTier.BuildWithIt);
        await _ui.DidNotReceive()
            .PickProfileAsync(
                Arg.Any<IReadOnlyList<ProfileChoice>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task BuildUpdatedLock_removes_out_of_profile_asset_and_writes_downloaded_asset()
    {
        // Arrange: two assets at different tiers.
        // "keep-asset"   is TryItOut  — will be in profile, needs Download (not on disk).
        // "remove-asset" is BuildWithIt — not in the selected profile (TryItOut), so planner
        //                                produces Remove for an asset already in the lock.
        var keepAsset = ManifestBuilder.Asset(
            "keep-asset",
            ProfileTier.TryItOut,
            installPath: "keep-asset.bin"
        );
        var removeAsset = ManifestBuilder.Asset(
            "remove-asset",
            ProfileTier.BuildWithIt,
            installPath: "remove-asset.bin"
        );
        var manifest = BuildManifest(keepAsset, removeAsset);

        // Place the remove-asset file on disk so we can assert it gets deleted.
        var removeAssetPath = Path.Combine(_tempDir, "remove-asset.bin");
        await File.WriteAllTextAsync(removeAssetPath, "stale-bytes");

        // Pre-write a lock that records "remove-asset" as already installed.
        var lockStore = new InstallStateLockStore(_lockPath);
        var existingLock = new InstallStateLock(
            SchemaVersion: 1,
            ManifestVersion: "v1",
            InstalledAt: DateTimeOffset.UtcNow,
            SelectedProfile: ProfileTier.TryItOut,
            Assets: new Dictionary<string, InstalledAssetRecord>
            {
                ["remove-asset"] = ManifestBuilder.Record(),
            },
            UserExclusions: Array.Empty<string>()
        );
        lockStore.Write(existingLock);

        _ui.PickProfileAsync(Arg.Any<IReadOnlyList<ProfileChoice>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ProfileTier.TryItOut));
        _ui.RunWithProgressAsync(
                Arg.Any<IReadOnlyList<AssetPlanItem>>(),
                Arg.Any<
                    Func<AssetPlanItem, IProgress<DownloadProgress>, CancellationToken, Task>
                >(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(true));

        var planner = new AssetPlanner(_tempDir);
        var runner = new BootstrapRunner(
            manifest,
            lockStore,
            planner,
            _downloader,
            _catalog,
            _ui,
            _gpuPreflight,
            _tempDir
        );

        // Act
        var result = await runner.RunAsync(
            new BootstrapOptions
            {
                Mode = BootstrapMode.AutoIfMissing,
                PreselectedProfile = ProfileTier.TryItOut,
            },
            CancellationToken.None
        );

        // Assert
        result.Success.Should().BeTrue();

        var writtenLock = lockStore.Read("v1");
        writtenLock
            .Assets.Should()
            .ContainKey("keep-asset", "downloaded asset must appear in lock");
        writtenLock
            .Assets.Should()
            .NotContainKey("remove-asset", "removed asset must be deleted from lock");

        File.Exists(removeAssetPath)
            .Should()
            .BeFalse("on-disk file for an out-of-profile asset must be deleted");
    }

    [Fact]
    public async Task ApplyRemovals_deletes_directory_install_paths()
    {
        var keepAsset = ManifestBuilder.Asset(
            "keep-asset",
            ProfileTier.TryItOut,
            installPath: "keep-asset.bin"
        );
        // Asset whose install path is a directory rather than a single file.
        var removeAsset = ManifestBuilder.Asset(
            "remove-bundle",
            ProfileTier.BuildWithIt,
            installPath: "remove-bundle"
        );
        var manifest = BuildManifest(keepAsset, removeAsset);

        var bundleDir = Path.Combine(_tempDir, "remove-bundle");
        Directory.CreateDirectory(bundleDir);
        await File.WriteAllTextAsync(Path.Combine(bundleDir, "a.bin"), "x");
        await File.WriteAllTextAsync(Path.Combine(bundleDir, "b.bin"), "y");

        var lockStore = new InstallStateLockStore(_lockPath);
        lockStore.Write(
            new InstallStateLock(
                SchemaVersion: 1,
                ManifestVersion: "v1",
                InstalledAt: DateTimeOffset.UtcNow,
                SelectedProfile: ProfileTier.TryItOut,
                Assets: new Dictionary<string, InstalledAssetRecord>
                {
                    ["remove-bundle"] = ManifestBuilder.Record(),
                },
                UserExclusions: Array.Empty<string>()
            )
        );

        _ui.RunWithProgressAsync(
                Arg.Any<IReadOnlyList<AssetPlanItem>>(),
                Arg.Any<
                    Func<AssetPlanItem, IProgress<DownloadProgress>, CancellationToken, Task>
                >(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(true));

        var planner = new AssetPlanner(_tempDir);
        var runner = new BootstrapRunner(
            manifest,
            lockStore,
            planner,
            _downloader,
            _catalog,
            _ui,
            _gpuPreflight,
            _tempDir
        );

        var result = await runner.RunAsync(
            new BootstrapOptions
            {
                Mode = BootstrapMode.AutoIfMissing,
                PreselectedProfile = ProfileTier.TryItOut,
            },
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        Directory
            .Exists(bundleDir)
            .Should()
            .BeFalse("directory install paths must be removed recursively");
    }

    [Fact]
    public async Task GpuPreflight_failure_with_user_abort_short_circuits_run()
    {
        var asset = BuildAsset("model-gpu");
        var manifest = BuildManifest(asset);

        _gpuPreflight
            .InspectAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    GpuStatus.Fail(GpuFailureKind.NoDriver, "No NVIDIA driver detected.")
                )
            );
        _ui.ShowGpuWarningAsync(Arg.Any<GpuStatus>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(GpuWarningResponse.Abort));

        var runner = BuildRunner(manifest);
        var result = await runner.RunAsync(
            new BootstrapOptions { Mode = BootstrapMode.AutoIfMissing },
            CancellationToken.None
        );

        // Abort: picker never runs, no downloads, no lock file, Success=false.
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("GPU preflight");
        await _ui.DidNotReceive()
            .PickProfileAsync(
                Arg.Any<IReadOnlyList<ProfileChoice>>(),
                Arg.Any<CancellationToken>()
            );
        await _downloader
            .DidNotReceive()
            .DownloadAsync(
                Arg.Any<AssetPlanItem>(),
                Arg.Any<IProgress<DownloadProgress>>(),
                Arg.Any<CancellationToken>()
            );
        File.Exists(_lockPath).Should().BeFalse("aborted run must not persist a lock");
    }

    [Fact]
    public async Task GpuPreflight_failure_with_user_continue_proceeds_to_install()
    {
        var asset = BuildAsset("model-gpu-ok");
        var manifest = BuildManifest(asset);

        _gpuPreflight
            .InspectAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    GpuStatus.Fail(
                        GpuFailureKind.DriverTooOld,
                        "Driver 560.30 is older than 580.65.",
                        driver: "560.30",
                        name: "Test GPU"
                    )
                )
            );
        _ui.ShowGpuWarningAsync(Arg.Any<GpuStatus>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(GpuWarningResponse.Continue));
        _ui.PickProfileAsync(Arg.Any<IReadOnlyList<ProfileChoice>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ProfileTier.TryItOut));
        _ui.RunWithProgressAsync(
                Arg.Any<IReadOnlyList<AssetPlanItem>>(),
                Arg.Any<
                    Func<AssetPlanItem, IProgress<DownloadProgress>, CancellationToken, Task>
                >(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(true));

        var runner = BuildRunner(manifest);
        var result = await runner.RunAsync(
            new BootstrapOptions { Mode = BootstrapMode.AutoIfMissing },
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        await _ui.Received(1)
            .ShowGpuWarningAsync(Arg.Any<GpuStatus>(), Arg.Any<CancellationToken>());
        await _ui.Received(1)
            .PickProfileAsync(
                Arg.Any<IReadOnlyList<ProfileChoice>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GpuPreflight_pass_does_not_show_warning_UI()
    {
        // Default _gpuPreflight already returns Pass in the constructor.
        var asset = BuildAsset("model-pass");
        var manifest = BuildManifest(asset);

        _ui.PickProfileAsync(Arg.Any<IReadOnlyList<ProfileChoice>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ProfileTier.TryItOut));
        _ui.RunWithProgressAsync(
                Arg.Any<IReadOnlyList<AssetPlanItem>>(),
                Arg.Any<
                    Func<AssetPlanItem, IProgress<DownloadProgress>, CancellationToken, Task>
                >(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(true));

        var runner = BuildRunner(manifest);
        await runner.RunAsync(
            new BootstrapOptions { Mode = BootstrapMode.AutoIfMissing },
            CancellationToken.None
        );

        await _ui.DidNotReceive()
            .ShowGpuWarningAsync(Arg.Any<GpuStatus>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SkipGpuCheck_flag_bypasses_preflight_entirely()
    {
        // Configure the preflight to fail hard — if it were called, this run
        // would abort. The --skip-gpu-check flag must short-circuit that path.
        _gpuPreflight
            .InspectAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(GpuStatus.Fail(GpuFailureKind.NoDriver, "Should never be seen."))
            );

        var asset = BuildAsset("model-skip");
        var manifest = BuildManifest(asset);

        _ui.PickProfileAsync(Arg.Any<IReadOnlyList<ProfileChoice>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ProfileTier.TryItOut));
        _ui.RunWithProgressAsync(
                Arg.Any<IReadOnlyList<AssetPlanItem>>(),
                Arg.Any<
                    Func<AssetPlanItem, IProgress<DownloadProgress>, CancellationToken, Task>
                >(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(true));

        var runner = BuildRunner(manifest);
        var result = await runner.RunAsync(
            new BootstrapOptions { Mode = BootstrapMode.AutoIfMissing, SkipGpuCheck = true },
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        await _gpuPreflight.DidNotReceive().InspectAsync(Arg.Any<CancellationToken>());
        await _ui.DidNotReceive()
            .ShowGpuWarningAsync(Arg.Any<GpuStatus>(), Arg.Any<CancellationToken>());
    }
}
