using System.IO.Compression;
using FluentAssertions;
using PersonaEngine.Lib.Bootstrapper.Sources;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests.Sources;

public class ZipExtractorTests : IDisposable
{
    private readonly string _tempDir;

    public ZipExtractorTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "ZipExtractorTests-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Extract_only_writes_requested_entries()
    {
        var zipBytes = BuildZip(
            new()
            {
                ["bin/cudart64_12.dll"] = new byte[] { 1, 2, 3 },
                ["bin/cublas64_12.dll"] = new byte[] { 4, 5, 6 },
                ["doc/README.md"] = new byte[] { 7 },
            }
        );

        await using var zip = new MemoryStream(zipBytes);
        await ZipExtractor.ExtractAsync(
            zip,
            new[] { "bin/cudart64_12.dll" },
            _tempDir,
            CancellationToken.None
        );

        File.Exists(Path.Combine(_tempDir, "bin", "cudart64_12.dll")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "bin", "cublas64_12.dll")).Should().BeFalse();
        File.Exists(Path.Combine(_tempDir, "doc", "README.md")).Should().BeFalse();
    }

    [Fact]
    public async Task Extract_preserves_nested_directory_structure()
    {
        var zipBytes = BuildZip(
            new()
            {
                ["embeddings/text_embedding_projected.npy"] = new byte[] { 0xAA, 0xBB },
                ["speakers/ryan/voice.npy"] = new byte[] { 0xCC, 0xDD },
                ["model_profile.json"] = new byte[] { 0xEE },
            }
        );

        await using var zip = new MemoryStream(zipBytes);
        await ZipExtractor.ExtractAsync(zip, wantedEntries: null, _tempDir, CancellationToken.None);

        File.Exists(Path.Combine(_tempDir, "embeddings", "text_embedding_projected.npy"))
            .Should()
            .BeTrue();
        File.Exists(Path.Combine(_tempDir, "speakers", "ryan", "voice.npy")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "model_profile.json")).Should().BeTrue();
    }

    [Fact]
    public async Task Extract_all_entries_when_filter_is_null()
    {
        var zipBytes = BuildZip(
            new() { ["a.txt"] = new byte[] { 1 }, ["b.txt"] = new byte[] { 2 } }
        );

        await using var zip = new MemoryStream(zipBytes);
        await ZipExtractor.ExtractAsync(zip, wantedEntries: null, _tempDir, CancellationToken.None);

        File.Exists(Path.Combine(_tempDir, "a.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "b.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task Extract_rejects_zip_slip_paths()
    {
        var zipBytes = BuildZipWithMaliciousPath();

        await using var zip = new MemoryStream(zipBytes);
        Func<Task> act = () =>
            ZipExtractor.ExtractAsync(zip, wantedEntries: null, _tempDir, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*path traversal*");
    }

    [Fact]
    public async Task Extract_uses_atomic_partial_rename()
    {
        var zipBytes = BuildZip(new() { ["x.txt"] = new byte[] { 9 } });

        await using var zip = new MemoryStream(zipBytes);
        await ZipExtractor.ExtractAsync(zip, wantedEntries: null, _tempDir, CancellationToken.None);

        Directory.GetFiles(_tempDir, "*.partial").Should().BeEmpty();
        File.Exists(Path.Combine(_tempDir, "x.txt")).Should().BeTrue();
    }

    private static byte[] BuildZip(Dictionary<string, byte[]> entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                var e = zip.CreateEntry(name);
                using var s = e.Open();
                s.Write(content, 0, content.Length);
            }

        return ms.ToArray();
    }

    private static byte[] BuildZipWithMaliciousPath()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = zip.CreateEntry("../../evil.txt");
            using var s = e.Open();
            s.WriteByte(0xff);
        }

        return ms.ToArray();
    }
}
