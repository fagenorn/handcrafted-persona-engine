using FluentAssertions;
using NSubstitute;
using PersonaEngine.Lib.Assets;
using PersonaEngine.Lib.Bootstrapper.Manifest;
using PersonaEngine.Lib.Bootstrapper.Planner;
using PersonaEngine.Lib.Bootstrapper.Sources;
using PersonaEngine.Lib.Bootstrapper.Tests.Helpers;
using Xunit;
using static PersonaEngine.Lib.Bootstrapper.Tests.Helpers.ManifestBuilder;

namespace PersonaEngine.Lib.Bootstrapper.Tests;

public sealed class BootstrapRunnerTests : IDisposable
{
    private readonly string _root;
    private readonly IAssetDownloader _downloader;
    private readonly IAssetCatalog _catalog;
    private readonly IBootstrapUserInterface _ui;

    public BootstrapRunnerTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "BootstrapRunnerTests-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_root);

        _downloader = Substitute.For<IAssetDownloader>();
        _catalog = Substitute.For<IAssetCatalog>();
        _ui = Substitute.For<IBootstrapUserInterface>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private BootstrapRunner BuildRunner(InstallManifest manifest)
    {
        var lockPath = Path.Combine(_root, "install-state.lock.json");
        var lockStore = new InstallStateLockStore(lockPath);
        var planner = new AssetPlanner(_root);
        return new BootstrapRunner(manifest, lockStore, planner, _downloader, _catalog, _ui, _root);
    }

    [Fact]
    public async Task AutoIfMissing_with_no_lock_runs_picker_and_downloads()
    {
        var asset = Asset("model-a", ProfileTier.TryItOut, installPath: "model-a.bin");
        var manifest = ManifestBuilder.Manifest("v1", asset);

        _ui.PickProfileAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<ProfileChoice?>(
                    new ProfileChoice(ProfileTier.TryItOut, Array.Empty<string>())
                )
            );
        _ui.ConfirmPlanAsync(Arg.Any<AssetPlan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        _downloader
            .DownloadAsync(
                Arg.Any<AssetPlanItem>(),
                Arg.Any<IProgress<long>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        var runner = BuildRunner(manifest);
        var result = await runner.RunAsync(
            new BootstrapOptions { Mode = BootstrapMode.AutoIfMissing },
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.ChangesApplied.Should().BeTrue();
        result.ActiveProfile.Should().Be(ProfileTier.TryItOut);
        await _downloader
            .Received(1)
            .DownloadAsync(
                Arg.Is<AssetPlanItem>(i => i.Entry.Id == "model-a"),
                Arg.Any<IProgress<long>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AutoIfMissing_with_everything_up_to_date_skips_download()
    {
        var asset = Asset("model-b", ProfileTier.TryItOut, installPath: "model-b.bin");
        var manifest = ManifestBuilder.Manifest("v1", asset);

        // Write a lock that says model-b is already installed with the current sha.
        var lockPath = Path.Combine(_root, "install-state.lock.json");
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
        var lockStore = new InstallStateLockStore(lockPath);
        lockStore.Write(existing);

        // Create the file on disk so AssetPlanner considers it present.
        var assetPath = Path.Combine(_root, "model-b.bin");
        await File.WriteAllTextAsync(assetPath, "dummy");

        // Runner built manually so we can pass in our pre-written lockStore.
        var planner = new AssetPlanner(_root);
        var runner = new BootstrapRunner(
            manifest,
            lockStore,
            planner,
            _downloader,
            _catalog,
            _ui,
            _root
        );

        var result = await runner.RunAsync(
            new BootstrapOptions { Mode = BootstrapMode.AutoIfMissing },
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.ChangesApplied.Should().BeFalse();
        result.ActiveProfile.Should().Be(ProfileTier.TryItOut);
        await _downloader
            .DidNotReceive()
            .DownloadAsync(
                Arg.Any<AssetPlanItem>(),
                Arg.Any<IProgress<long>>(),
                Arg.Any<CancellationToken>()
            );
        await _ui.DidNotReceive().PickProfileAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PickProfile_returns_null_aborts_run()
    {
        var manifest = ManifestBuilder.Manifest("v1", Asset("model-c", ProfileTier.TryItOut));

        _ui.PickProfileAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ProfileChoice?>(null));

        var runner = BuildRunner(manifest);
        var result = await runner.RunAsync(
            new BootstrapOptions { Mode = BootstrapMode.AutoIfMissing },
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.ChangesApplied.Should().BeFalse();
        await _downloader
            .DidNotReceive()
            .DownloadAsync(
                Arg.Any<AssetPlanItem>(),
                Arg.Any<IProgress<long>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ConfirmPlan_returns_false_aborts_run()
    {
        var asset = Asset("model-d", ProfileTier.TryItOut, installPath: "model-d.bin");
        var manifest = ManifestBuilder.Manifest("v1", asset);

        _ui.PickProfileAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<ProfileChoice?>(
                    new ProfileChoice(ProfileTier.TryItOut, Array.Empty<string>())
                )
            );
        _ui.ConfirmPlanAsync(Arg.Any<AssetPlan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var runner = BuildRunner(manifest);
        var result = await runner.RunAsync(
            new BootstrapOptions { Mode = BootstrapMode.AutoIfMissing },
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.ChangesApplied.Should().BeFalse();
        await _downloader
            .DidNotReceive()
            .DownloadAsync(
                Arg.Any<AssetPlanItem>(),
                Arg.Any<IProgress<long>>(),
                Arg.Any<CancellationToken>()
            );
    }
}
