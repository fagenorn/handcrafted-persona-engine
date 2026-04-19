using FluentAssertions;
using PersonaEngine.Lib.Bootstrapper.Manifest;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests.Manifest;

public class InstallStateLockStoreTests : IDisposable
{
    private readonly string _tempDir;

    public InstallStateLockStoreTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "InstallStateLockStoreTests-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Read_returns_empty_when_lock_file_missing()
    {
        var store = new InstallStateLockStore(Path.Combine(_tempDir, "missing.json"));

        var lockState = store.Read("manifest-v");

        lockState.Should().NotBeNull();
        lockState.Assets.Should().BeEmpty();
        lockState.ManifestVersion.Should().Be("manifest-v");
    }

    [Fact]
    public void Write_then_read_roundtrips_state()
    {
        var path = Path.Combine(_tempDir, "lock.json");
        var store = new InstallStateLockStore(path);
        var original = new InstallStateLock(
            SchemaVersion: 1,
            ManifestVersion: "v1",
            InstalledAt: DateTimeOffset.UtcNow,
            SelectedProfile: ProfileTier.StreamWithIt,
            Assets: new Dictionary<string, InstalledAssetRecord>
            {
                ["tts.kokoro.model"] = new("v1", "abc", DateTimeOffset.UtcNow),
            },
            UserExclusions: new[] { "tts.qwen3.model" }
        );

        store.Write(original);
        var roundtripped = store.Read("v1");

        roundtripped.SelectedProfile.Should().Be(ProfileTier.StreamWithIt);
        roundtripped.Assets.Should().ContainKey("tts.kokoro.model");
        roundtripped.UserExclusions.Should().ContainSingle().Which.Should().Be("tts.qwen3.model");
    }

    [Fact]
    public void Write_is_atomic_via_tmp_rename()
    {
        var path = Path.Combine(_tempDir, "lock.json");
        var store = new InstallStateLockStore(path);

        store.Write(InstallStateLock.Empty("v1"));

        File.Exists(path + ".tmp")
            .Should()
            .BeFalse("the .tmp file should be renamed away after a successful write");
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void Read_returns_empty_when_lock_file_is_corrupt()
    {
        var path = Path.Combine(_tempDir, "lock.json");
        File.WriteAllText(path, "{ not valid json");
        var store = new InstallStateLockStore(path);

        var lockState = store.Read("v1");

        lockState.Assets.Should().BeEmpty();
    }
}
