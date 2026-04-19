using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonaEngine.Lib.Assets.Manifest;

public sealed class AssetSourceJsonConverter : JsonConverter<AssetSource>
{
    public override AssetSource Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var typeProp))
            throw new JsonException("AssetSource missing 'type' discriminator");

        var typeStr = typeProp.GetString() ?? throw new JsonException("AssetSource 'type' is null");

        if (!Enum.TryParse<SourceType>(typeStr, ignoreCase: false, out var sourceType))
            throw new JsonException($"Unknown AssetSource type '{typeStr}'");

        var rawJson = root.GetRawText();
        return sourceType switch
        {
            SourceType.NvidiaRedist => JsonSerializer.Deserialize<NvidiaRedistSource>(
                rawJson,
                OptionsWithoutThisConverter(options)
            ) ?? throw new JsonException("NvidiaRedistSource deserialization returned null"),
            SourceType.HuggingFace => JsonSerializer.Deserialize<HuggingFaceSource>(
                rawJson,
                OptionsWithoutThisConverter(options)
            ) ?? throw new JsonException("HuggingFaceSource deserialization returned null"),
            _ => throw new JsonException($"Unhandled SourceType '{sourceType}'"),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        AssetSource value,
        JsonSerializerOptions options
    )
    {
        switch (value)
        {
            case NvidiaRedistSource nv:
                JsonSerializer.Serialize(writer, nv, OptionsWithoutThisConverter(options));
                break;
            case HuggingFaceSource hf:
                JsonSerializer.Serialize(writer, hf, OptionsWithoutThisConverter(options));
                break;
            default:
                throw new JsonException($"Unhandled AssetSource type '{value.GetType()}'");
        }
    }

    // Avoid recursion: clone options without this converter so nested calls use the default behavior
    // for derived records. We add JsonStringEnumConverter so the inherited Type property is
    // serialized as a string discriminator (e.g. "NvidiaRedist") rather than an integer.
    private static JsonSerializerOptions OptionsWithoutThisConverter(JsonSerializerOptions options)
    {
        var clone = new JsonSerializerOptions(options);
        for (var i = clone.Converters.Count - 1; i >= 0; i--)
            if (clone.Converters[i] is AssetSourceJsonConverter)
                clone.Converters.RemoveAt(i);

        if (!clone.Converters.Any(c => c is JsonStringEnumConverter))
            clone.Converters.Add(new JsonStringEnumConverter());

        return clone;
    }
}
