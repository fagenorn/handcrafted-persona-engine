using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonaEngine.Lib.Assets.Manifest;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext" /> for the install manifest and
/// lock file. Reading/writing through this context avoids reflection-based JSON at
/// runtime (cheaper startup, trim-safe) and is the single source of truth for the
/// options used by <see cref="ManifestLoader" /> and <see cref="InstallStateLockStore" />.
/// Polymorphism is handled by the <c>[JsonPolymorphic]</c> / <c>[JsonDerivedType]</c>
/// attributes on <see cref="AssetSource" /> — there's no custom converter anymore.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(InstallManifest))]
[JsonSerializable(typeof(InstallStateLock))]
[JsonSerializable(typeof(AssetSource))]
[JsonSerializable(typeof(NvidiaRedistSource))]
[JsonSerializable(typeof(HuggingFaceSource))]
public sealed partial class ManifestJsonContext : JsonSerializerContext { }
