using System.Text.Json.Serialization;

namespace PersonaEngine.Lib.Bootstrapper.Manifest;

[JsonConverter(typeof(AssetSourceJsonConverter))]
public abstract record AssetSource(SourceType Type)
{
    /// <summary>
    /// Free-form version string recorded in the install lock so we can detect when an asset
    /// was installed from an out-of-date source pin. NVIDIA → archive Version; HuggingFace → Revision.
    /// </summary>
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
