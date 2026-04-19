using System.IO.Compression;

namespace PersonaEngine.Lib.Bootstrapper.Sources;

public static class ZipExtractor
{
    public static async Task ExtractAsync(
        Stream zipStream,
        IReadOnlyList<string>? wantedEntries,
        string targetDirectory,
        CancellationToken ct
    )
    {
        Directory.CreateDirectory(targetDirectory);
        var fullTarget = Path.GetFullPath(targetDirectory);

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            if (
                wantedEntries is not null
                && !wantedEntries.Contains(entry.FullName, StringComparer.OrdinalIgnoreCase)
            )
                continue;

            // Skip pure directory entries (Name is empty when FullName ends with '/').
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            // Reject any raw entry path that contains path-traversal segments.
            // We check FullName before resolving because the resolved path could
            // happen to land inside fullTarget by coincidence even when the
            // archive author was being malicious.
            var normalised = entry.FullName.Replace('\\', '/');
            foreach (var segment in normalised.Split('/'))
            {
                if (segment == "..")
                    throw new InvalidOperationException(
                        $"Refusing to extract '{entry.FullName}': path traversal / zip slip detected."
                    );
            }

            // Preserve the in-zip directory structure under fullTarget so bundles
            // like qwen3-tts (embeddings/, speakers/) and wav2vec2 (onnx/) land
            // at the paths their runtime consumers expect.
            var dest = Path.GetFullPath(Path.Combine(fullTarget, entry.FullName));

            // Secondary guard: resolved destination must still be inside the
            // target directory. Catches any traversal we missed (e.g. archives
            // that use absolute Windows paths or unusual separator combos).
            if (
                !dest.StartsWith(fullTarget + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && dest != fullTarget
            )
                throw new InvalidOperationException(
                    $"Refusing to extract '{entry.FullName}': resolved path '{dest}' would escape target directory (path traversal / zip slip)."
                );

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            var partial = dest + ".partial";
            await using (var src = entry.Open())
            await using (var fs = File.Create(partial))
                await src.CopyToAsync(fs, ct).ConfigureAwait(false);

            if (File.Exists(dest))
                File.Delete(dest);
            File.Move(partial, dest);
        }
    }
}
