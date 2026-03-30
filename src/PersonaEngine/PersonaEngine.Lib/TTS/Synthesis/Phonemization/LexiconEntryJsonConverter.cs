using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Custom JSON converter for lexicon entries
/// </summary>
public class LexiconEntryJsonConverter : JsonConverter<LexiconEntry>
{
    public override LexiconEntry Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        var entry = new LexiconEntry();

        if (reader.TokenType == JsonTokenType.String)
        {
            entry.Simple = reader.GetString();
        }
        else if (reader.TokenType == JsonTokenType.StartObject)
        {
            entry.ByTag = JsonSerializer.Deserialize<Dictionary<string, string>>(
                ref reader,
                options
            );
        }
        else
        {
            throw new JsonException(
                $"Unexpected token {reader.TokenType} when deserializing lexicon entry"
            );
        }

        return entry;
    }

    public override void Write(
        Utf8JsonWriter writer,
        LexiconEntry value,
        JsonSerializerOptions options
    )
    {
        if (value.IsSimple)
        {
            writer.WriteStringValue(value.Simple);
        }
        else
        {
            JsonSerializer.Serialize(writer, value.ByTag, options);
        }
    }
}
