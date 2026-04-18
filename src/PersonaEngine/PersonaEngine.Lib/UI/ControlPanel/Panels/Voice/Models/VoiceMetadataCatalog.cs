using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Models;

/// <summary>
///     Loads the embedded <c>voice_metadata.json</c> resource at construction and exposes
///     lookup + enumeration of <see cref="VoiceDescriptor" /> instances keyed by (engine, id).
///     Unknown voices resolve to <see cref="VoiceDescriptor.Fallback" />.
/// </summary>
public sealed class VoiceMetadataCatalog
{
    private const string ResourceName = "PersonaEngine.Lib.Resources.voice_metadata.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly FrozenDictionary<(VoiceEngine Engine, string Id), VoiceDescriptor> _byKey;
    private readonly FrozenDictionary<VoiceEngine, IReadOnlyList<VoiceDescriptor>> _byEngine;

    public VoiceMetadataCatalog()
    {
        var descriptors = LoadFromEmbeddedResource();

        _byKey = descriptors.ToFrozenDictionary(d => (d.Engine, d.Id));
        _byEngine = descriptors
            .GroupBy(d => d.Engine)
            .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<VoiceDescriptor>)g.ToArray());
    }

    public VoiceDescriptor Resolve(VoiceEngine engine, string id) =>
        _byKey.TryGetValue((engine, id), out var descriptor)
            ? descriptor
            : VoiceDescriptor.Fallback(engine, id);

    public IReadOnlyList<VoiceDescriptor> List(VoiceEngine engine) =>
        _byEngine.TryGetValue(engine, out var list) ? list : [];

    private static VoiceDescriptor[] LoadFromEmbeddedResource()
    {
        var assembly = typeof(VoiceMetadataCatalog).Assembly;
        using var stream =
            assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. Check .csproj and file path."
            );

        var payload =
            JsonSerializer.Deserialize<CatalogPayload>(stream, JsonOpts)
            ?? throw new InvalidOperationException("voice_metadata.json deserialized to null.");

        return payload.Voices;
    }

    private sealed record CatalogPayload
    {
        public VoiceDescriptor[] Voices { get; init; } = [];
    }
}
