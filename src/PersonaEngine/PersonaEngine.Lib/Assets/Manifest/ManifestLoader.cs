using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonaEngine.Lib.Assets.Manifest;

public static class ManifestLoader
{
    public const int SupportedSchemaVersion = 1;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new AssetSourceJsonConverter(), new JsonStringEnumConverter() },
    };

    public static InstallManifest LoadFromJson(string json)
    {
        var manifest =
            JsonSerializer.Deserialize<InstallManifest>(json, JsonOptions)
            ?? throw new JsonException("Manifest deserialized to null");

        if (manifest.SchemaVersion != SupportedSchemaVersion)
            throw new NotSupportedException(
                $"install-manifest.json schemaVersion {manifest.SchemaVersion} is not supported by this build "
                    + $"(expected {SupportedSchemaVersion}). Update PersonaEngine.App.exe."
            );

        return manifest;
    }

    public static InstallManifest LoadEmbedded()
    {
        var assembly = typeof(ManifestLoader).Assembly;
        var resourceName =
            assembly
                .GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("install-manifest.json", StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                "Embedded resource install-manifest.json not found in PersonaEngine.Lib assembly."
            );

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return LoadFromJson(reader.ReadToEnd());
    }
}
