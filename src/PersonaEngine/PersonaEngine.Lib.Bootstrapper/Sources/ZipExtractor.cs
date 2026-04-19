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

            // Reject any raw entry path that contains path-traversal segments.
            // We check FullName before stripping because Path.GetFileName would silently
            // neutralise "../../evil.txt" → "evil.txt", hiding the malicious intent.
            var normalised = entry.FullName.Replace('\\', '/');
            foreach (var segment in normalised.Split('/'))
            {
                if (segment == "..")
                    throw new InvalidOperationException(
                        $"Refusing to extract '{entry.FullName}': path traversal / zip slip detected."
                    );
            }

            // Strip directory prefix — write the file flat into targetDirectory.
            var fileName = Path.GetFileName(entry.FullName);
            if (string.IsNullOrEmpty(fileName))
                continue; // pure directory entries

            var dest = Path.GetFullPath(Path.Combine(fullTarget, fileName));

            // Secondary guard: resolved destination must still be inside the target directory.
            if (
                !dest.StartsWith(fullTarget + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && dest != fullTarget
            )
                throw new InvalidOperationException(
                    $"Refusing to extract '{entry.FullName}': resolved path '{dest}' would escape target directory (path traversal / zip slip)."
                );

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
