using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonaEngine.Lib.TTS.Synthesis.Qwen3;

/// <summary>
///     Deserialized model_profile.json for Qwen3-TTS (LunaVox format).
///     Flat structure matching LunaVox's model_profile.json schema.
/// </summary>
internal sealed class Qwen3ModelConfig
{
    [JsonPropertyName("codec_pad_id")]
    public int CodecPadId { get; set; }

    [JsonPropertyName("codec_bos_id")]
    public int CodecBosId { get; set; }

    [JsonPropertyName("codec_eos_id")]
    public int CodecEosId { get; set; }

    [JsonPropertyName("codec_think_id")]
    public int CodecThinkId { get; set; }

    [JsonPropertyName("codec_nothink_id")]
    public int CodecNothinkId { get; set; }

    [JsonPropertyName("codec_think_bos_id")]
    public int CodecThinkBosId { get; set; }

    [JsonPropertyName("codec_think_eos_id")]
    public int CodecThinkEosId { get; set; }

    [JsonPropertyName("codec_num_codebooks")]
    public int CodecNumCodebooks { get; set; } = 16;

    [JsonPropertyName("tts_pad_id")]
    public int TtsPadId { get; set; }

    [JsonPropertyName("tts_bos_id")]
    public int TtsBosId { get; set; }

    [JsonPropertyName("tts_eos_id")]
    public int TtsEosId { get; set; }

    [JsonPropertyName("language_names")]
    public string[] LanguageNames { get; set; } = [];

    [JsonPropertyName("language_ids")]
    public int[] LanguageIdValues { get; set; } = [];

    private Dictionary<string, int>? _languageIdMap;

    /// <summary>
    ///     Language name → codec token ID dictionary, computed from the parallel arrays.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, int> LanguageIds =>
        _languageIdMap ??= LanguageNames
            .Zip(LanguageIdValues)
            .ToDictionary(p => p.First, p => p.Second, StringComparer.OrdinalIgnoreCase);

    public static Qwen3ModelConfig Load(string path)
    {
        var json = File.ReadAllText(path);

        var config =
            JsonSerializer.Deserialize<Qwen3ModelConfig>(json)
            ?? throw new InvalidDataException($"Failed to deserialize config from {path}");

        if (config.CodecEosId == 0)
        {
            throw new InvalidDataException(
                $"codec_eos_id is 0 in {path} — likely a deserialization mismatch. "
                    + $"Check that the JSON structure matches {nameof(Qwen3ModelConfig)}. "
                    + $"Raw JSON (first 500 chars): {json[..Math.Min(500, json.Length)]}"
            );
        }

        return config;
    }
}

/// <summary>
///     Generation parameters for Qwen3-TTS inference.
/// </summary>
public sealed class Qwen3GenerationOptions
{
    public float Temperature { get; init; } = 0.6f;

    public int TopK { get; init; } = 50;

    public float TopP { get; init; } = 1.0f;

    public float RepetitionPenalty { get; init; } = 1.05f;

    public int MaxNewTokens { get; init; } = 500;

    public bool CodePredictorGreedy { get; init; }

    public bool SilencePenaltyEnabled { get; init; }
}
