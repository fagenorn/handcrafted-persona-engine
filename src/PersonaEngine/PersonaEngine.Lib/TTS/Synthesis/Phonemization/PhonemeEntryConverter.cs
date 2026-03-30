using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonaEngine.Lib.TTS.Synthesis;

public class PhonemeEntryConverter : JsonConverter<PhonemeEntry>
{
    public override PhonemeEntry Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var doc = JsonDocument.ParseValue(ref reader);

        return PhonemeEntry.FromJsonElement(doc.RootElement);
    }

    public override void Write(
        Utf8JsonWriter writer,
        PhonemeEntry value,
        JsonSerializerOptions options
    )
    {
        switch (value)
        {
            case SimplePhonemeEntry simple:
                writer.WriteStringValue(simple.Phoneme);

                break;
            case ContextualPhonemeEntry contextual:
                writer.WriteStartObject();
                foreach (var form in contextual.Forms)
                {
                    writer.WriteString(form.Key, form.Value);
                }

                writer.WriteEndObject();

                break;
            default:
                throw new JsonException($"Unsupported PhonemeEntry type: {value.GetType()}");
        }
    }
}
