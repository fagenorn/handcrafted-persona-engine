using System.Text.Json.Serialization;

namespace PersonaEngine.Lib.Assets.Manifest;

/// <summary>
/// Polymorphic base for every asset source in the install manifest. The
/// <c>type</c> discriminator written on the wire is handled by System.Text.Json
/// source-gen (see <see cref="ManifestJsonContext" />) — the runtime <see cref="Type" />
/// property is populated by the derived record ctor and marked
/// <see cref="JsonIgnoreAttribute" /> so we don't duplicate the discriminator
/// on write.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NvidiaRedistSource), typeDiscriminator: nameof(SourceType.NvidiaRedist))]
[JsonDerivedType(typeof(HuggingFaceSource), typeDiscriminator: nameof(SourceType.HuggingFace))]
public abstract record AssetSource([property: JsonIgnore] SourceType Type)
{
    /// <summary>
    /// Free-form version string recorded in the install lock so we can detect when an asset
    /// was installed from an out-of-date source pin. NVIDIA → archive Version; HuggingFace → Revision.
    /// </summary>
    [JsonIgnore]
    public abstract string SourceVersion { get; }
}

public sealed record NvidiaRedistSource(
    string Channel,
    string Package,
    string Version,
    string Platform,
    IReadOnlyList<string> ExtractFiles
) : AssetSource(SourceType.NvidiaRedist)
{
    public override string SourceVersion => Version;
}

public sealed record HuggingFaceSource(string Repo, string Revision, string Path)
    : AssetSource(SourceType.HuggingFace)
{
    public override string SourceVersion => Revision;
}
