using System.Text.Json;

namespace PersonaEngine.Lib.Bootstrapper.Sources;

public sealed record NvidiaRedistPlatform(
    string RelativePath,
    string Sha256,
    string Md5,
    long Size
);

public sealed record NvidiaRedistPackage(
    string Name,
    string License,
    string Version,
    IReadOnlyDictionary<string, NvidiaRedistPlatform> Platforms
);

public sealed class NvidiaRedistManifest
{
    public IReadOnlyDictionary<string, NvidiaRedistPackage> Packages { get; }

    private NvidiaRedistManifest(IReadOnlyDictionary<string, NvidiaRedistPackage> packages)
    {
        Packages = packages;
    }

    public static NvidiaRedistManifest Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var packages = new Dictionary<string, NvidiaRedistPackage>(StringComparer.Ordinal);

        foreach (var prop in root.EnumerateObject())
        {
            // Skip metadata fields like "release_date" / "release_label" — packages are objects with platforms inside.
            if (prop.Value.ValueKind != JsonValueKind.Object)
                continue;
            if (!prop.Value.TryGetProperty("version", out _))
                continue; // not a package

            var name = prop.Value.GetProperty("name").GetString() ?? prop.Name;
            var license = prop.Value.TryGetProperty("license", out var lic)
                ? lic.GetString() ?? ""
                : "";
            var version = prop.Value.GetProperty("version").GetString() ?? "";

            var platforms = new Dictionary<string, NvidiaRedistPlatform>(StringComparer.Ordinal);
            foreach (var sub in prop.Value.EnumerateObject())
            {
                if (sub.Value.ValueKind != JsonValueKind.Object)
                    continue;
                if (!sub.Value.TryGetProperty("relative_path", out _))
                    continue;

                platforms[sub.Name] = new NvidiaRedistPlatform(
                    RelativePath: sub.Value.GetProperty("relative_path").GetString() ?? "",
                    Sha256: sub.Value.GetProperty("sha256").GetString() ?? "",
                    Md5: sub.Value.TryGetProperty("md5", out var md5) ? md5.GetString() ?? "" : "",
                    Size: ParseSize(sub.Value.GetProperty("size"))
                );
            }

            packages[prop.Name] = new NvidiaRedistPackage(name, license, version, platforms);
        }

        return new NvidiaRedistManifest(packages);
    }

    private static long ParseSize(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.Number => el.GetInt64(),
            JsonValueKind.String when long.TryParse(el.GetString(), out var n) => n,
            _ => throw new FormatException($"Unexpected 'size' representation: {el.ValueKind}"),
        };
}
