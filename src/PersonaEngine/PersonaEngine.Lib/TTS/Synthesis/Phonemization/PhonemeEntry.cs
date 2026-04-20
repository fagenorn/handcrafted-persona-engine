using System.Text.Json;

namespace PersonaEngine.Lib.TTS.Synthesis;

public abstract record PhonemeEntry
{
    // Factory method to create the appropriate entry type from JSON
    public static PhonemeEntry FromJsonElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return new SimplePhonemeEntry(element.GetString()!);
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    dict[property.Name] = property.Value.GetString()!;
                }
            }

            return new ContextualPhonemeEntry(dict);
        }

        throw new JsonException($"Unexpected JSON element type: {element.ValueKind}");
    }
}
