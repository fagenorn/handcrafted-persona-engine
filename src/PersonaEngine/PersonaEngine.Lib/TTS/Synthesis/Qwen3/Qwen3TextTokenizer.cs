using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.Tokenizers;

namespace PersonaEngine.Lib.TTS.Synthesis.Qwen3;

/// <summary>
///     Qwen3-TTS tokenizer backed by Microsoft.ML.Tokenizers BpeTokenizer.
///     Uses byte-level BPE with GPT-2 pre-tokenization regex.
/// </summary>
internal sealed class Qwen3TextTokenizer
{
    // Qwen special token IDs
    public const int EndOfTextId = 151643;
    public const int ImStartId = 151644;
    public const int ImEndId = 151645;
    public const int AudioStartId = 151669;
    public const int AudioEndId = 151670;
    public const int TtsPadId = 151671;
    public const int TtsBosId = 151672;
    public const int TtsEodId = 151673;
    public const int TtsBosSingleId = 151674;
    public const int AudioPadId = 151675;
    public const int AssistantId = 77091;
    public const int NewlineId = 198;

    private static readonly Regex Gpt2Regex = new(
        @"(?i:'s|'t|'re|'ve|'m|'ll|'d)|[^\r\n\p{L}\p{N}]?\p{L}+|\p{N}{1,3}| ?[^\s\p{L}\p{N}]+[\r\n]*|\s*[\r\n]+|\s+(?!\S)|\s+",
        RegexOptions.Compiled
    );

    private static readonly Dictionary<string, int> SpecialTokens = new()
    {
        ["<|endoftext|>"] = EndOfTextId,
        ["<|im_start|>"] = ImStartId,
        ["<|im_end|>"] = ImEndId,
        ["<|audio_start|>"] = AudioStartId,
        ["<|audio_end|>"] = AudioEndId,
        ["<tts_pad>"] = TtsPadId,
        ["<tts_text_bos>"] = TtsBosId,
        ["<tts_text_eod>"] = TtsEodId,
        ["<tts_text_bos_single>"] = TtsBosSingleId,
        ["<|audio_pad|>"] = AudioPadId,
    };

    private readonly BpeTokenizer _tokenizer;

    private Qwen3TextTokenizer(BpeTokenizer tokenizer)
    {
        _tokenizer = tokenizer;
    }

    public static Qwen3TextTokenizer Load(string tokenizerJsonPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(tokenizerJsonPath));
        var model = doc.RootElement.GetProperty("model");

        var vocab = model
            .GetProperty("vocab")
            .EnumerateObject()
            .Select(p => new KeyValuePair<string, int>(p.Name, p.Value.GetInt32()));

        var merges = model
            .GetProperty("merges")
            .EnumerateArray()
            .Select(e =>
                e.ValueKind == JsonValueKind.String
                    ? e.GetString()!
                    : string.Join(' ', e.EnumerateArray().Select(t => t.GetString()!))
            );

        var preTokenizer = new RegexPreTokenizer(Gpt2Regex, SpecialTokens);

        var options = new BpeOptions(vocab)
        {
            Merges = merges,
            PreTokenizer = preTokenizer,
            SpecialTokens = SpecialTokens,
            ByteLevel = true,
            FuseUnknownTokens = false,
        };

        return new Qwen3TextTokenizer(BpeTokenizer.Create(options));
    }

    /// <summary>
    ///     Encodes text into token IDs.
    /// </summary>
    public int[] Encode(string text)
    {
        var ids = _tokenizer.EncodeToIds(text);

        return ids.ToArray();
    }

    /// <summary>
    ///     Decodes a single token ID back to its text representation.
    /// </summary>
    public string Decode(int tokenId)
    {
        return _tokenizer.Decode([tokenId]);
    }

    /// <summary>
    ///     Builds the full token ID sequence for CustomVoice inference.
    ///     Returns (prefixTokens, textTokens, suffixTokens) split for embedding construction.
    /// </summary>
    public (int[] Prefix, int[] TextBody) BuildCustomVoicePrompt(
        string text,
        string? instruct = null
    )
    {
        var prefix = new[] { ImStartId, AssistantId, NewlineId };
        var textTokens = Encode(text);
        var unused = new[] { ImEndId, NewlineId, ImStartId, AssistantId, NewlineId }; // Suffix

        return (prefix, textTokens);
    }
}
