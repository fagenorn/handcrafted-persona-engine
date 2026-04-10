// ReSharper disable NotAccessedField.Local

using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Text.Json;
using PersonaEngine.Lib.Utils.IO;

namespace PersonaEngine.Lib.TTS.Synthesis.Qwen3;

/// <summary>
///     Embedding manager for the GGUF/llama.cpp path.
///     All embeddings are 2048-dim (Talker's native dimension).
///     The Predictor receives 1024-dim projections via proj_weight/proj_bias.
/// </summary>
internal sealed class GgufEmbeddingManager
{
    private const int TalkerDim = 2048;
    private const int PredictorDim = 1024;

    // Text embeddings: [vocab_size, 2048]
    private readonly float[] _textEmbedding;
    private readonly int _textVocabSize;

    // 16 codec embedding tables, all [entries, 2048]
    private readonly float[][] _codecEmbeddings;
    private readonly int[] _codecVocabSizes;

    // Projection: 2048 → 1024 for Predictor input
    private readonly float[] _projWeight; // [1024, 2048]
    private readonly float[] _projBias; // [1024]

    // Speaker embeddings: name → 2048-dim embedding from preset JSON files
    private readonly Dictionary<string, float[]> _speakerEmbeddings;

    // Config for codec control token IDs
    private readonly Qwen3ModelConfig _config;

    // Special: tts_pad text embedding (pre-cached, 2048-dim)
    private readonly float[] _ttsPadEmbedding;

    private GgufEmbeddingManager(
        Qwen3ModelConfig config,
        float[] textEmbedding,
        int textVocabSize,
        float[][] codecEmbeddings,
        int[] codecVocabSizes,
        float[] projWeight,
        float[] projBias,
        Dictionary<string, float[]> speakerEmbeddings
    )
    {
        _config = config;
        _textEmbedding = textEmbedding;
        _textVocabSize = textVocabSize;
        _codecEmbeddings = codecEmbeddings;
        _codecVocabSizes = codecVocabSizes;
        _projWeight = projWeight;
        _projBias = projBias;
        _speakerEmbeddings = speakerEmbeddings;

        // Pre-cache tts_pad embedding (token 151671)
        _ttsPadEmbedding = new float[TalkerDim];
        GetTextEmbedding(config.TtsPadId).CopyTo(_ttsPadEmbedding);
    }

    public static GgufEmbeddingManager Load(
        string embeddingsDir,
        string speakersDir,
        Qwen3ModelConfig config
    )
    {
        var (textEmb, textShape) = NpyReader.ReadFloat32(
            Path.Combine(embeddingsDir, "text_embedding_projected.npy")
        );
        var textVocabSize = textShape[0];

        var (projW, _) = NpyReader.ReadFloat32(Path.Combine(embeddingsDir, "proj_weight.npy"));
        var (projB, _) = NpyReader.ReadFloat32(Path.Combine(embeddingsDir, "proj_bias.npy"));

        // Load 16 codec embedding tables (groups 0-15)
        var codecEmbeddings = new float[16][];
        var codecVocabSizes = new int[16];

        for (var i = 0; i < 16; i++)
        {
            var (codecEmb, codecShape) = NpyReader.ReadFloat32(
                Path.Combine(embeddingsDir, $"codec_embedding_{i}.npy")
            );
            codecEmbeddings[i] = codecEmb;
            codecVocabSizes[i] = codecShape[0];
        }

        // Load preset speaker embeddings from speakers/ directory.
        // Each JSON file contains a 2048-dim spk_emb vector.
        var speakerEmbeddings = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(speakersDir))
        {
            foreach (var file in Directory.GetFiles(speakersDir, "*.json"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (fileName == "index")
                {
                    continue;
                }

                try
                {
                    var doc = JsonDocument.Parse(File.ReadAllText(file));
                    if (doc.RootElement.TryGetProperty("spk_emb", out var embArr))
                    {
                        var emb = new float[TalkerDim];
                        var idx = 0;
                        foreach (var val in embArr.EnumerateArray())
                        {
                            if (idx < TalkerDim)
                            {
                                emb[idx++] = val.GetSingle();
                            }
                        }

                        speakerEmbeddings[fileName] = emb;
                    }
                }
                catch
                {
                    // Skip malformed speaker files
                }
            }
        }

        return new GgufEmbeddingManager(
            config,
            textEmb,
            textVocabSize,
            codecEmbeddings,
            codecVocabSizes,
            projW,
            projB,
            speakerEmbeddings
        );
    }

    /// <summary>
    ///     Looks up a 2048-dim text embedding.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<float> GetTextEmbedding(int tokenId)
    {
        return _textEmbedding.AsSpan(tokenId * TalkerDim, TalkerDim);
    }

    /// <summary>
    ///     Looks up a 2048-dim codec embedding for a given group and code.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<float> GetCodecEmbedding(int group, int code)
    {
        return _codecEmbeddings[group].AsSpan(code * TalkerDim, TalkerDim);
    }

    /// <summary>
    ///     Projects a 2048-dim codec embedding to 1024-dim for the Predictor.
    ///     output = proj_weight(1024, 2048) @ input(2048) + proj_bias(1024)
    /// </summary>
    public void ProjectTo1024(ReadOnlySpan<float> input2048, Span<float> output1024)
    {
        for (var i = 0; i < PredictorDim; i++)
        {
            output1024[i] =
                TensorPrimitives.Dot(
                    _projWeight.AsSpan(i * TalkerDim, TalkerDim),
                    input2048[..TalkerDim]
                ) + _projBias[i];
        }
    }

    /// <summary>
    ///     Gets a 1024-dim projected codec embedding for the Predictor.
    /// </summary>
    public float[] GetCodecEmbedding1024(int group, int code)
    {
        var result = new float[PredictorDim];
        ProjectTo1024(GetCodecEmbedding(group, code), result);

        return result;
    }

    /// <summary>
    ///     Builds the codec prefix token sequence for CustomVoice generation.
    /// </summary>
    /// <summary>
    ///     Builds the codec prefix (control section only, without speaker).
    ///     Speaker is handled separately in BuildPrefillEmbedding using the
    ///     pre-extracted 2048-dim speaker embedding from preset files.
    /// </summary>
    public int[] BuildCodecPrefix(string language)
    {
        var prefix = new List<int>(8);

        if (language.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            prefix.Add(_config.CodecNothinkId);
        }
        else
        {
            prefix.Add(_config.CodecThinkId);
        }

        prefix.Add(_config.CodecThinkBosId);

        if (
            !language.Equals("auto", StringComparison.OrdinalIgnoreCase)
            && _config.LanguageIds.TryGetValue(language.ToLowerInvariant(), out var langId)
        )
        {
            prefix.Add(langId);
        }

        prefix.Add(_config.CodecThinkEosId);

        return prefix.ToArray();
    }

    /// <summary>
    ///     Gets the 2048-dim speaker embedding for a given speaker name.
    ///     Returns null if the speaker is not found.
    /// </summary>
    public float[]? GetSpeakerEmbedding(string speaker)
    {
        return _speakerEmbeddings.GetValueOrDefault(speaker);
    }

    /// <summary>
    ///     Builds the prefill embedding for the Talker.
    ///     Prompt structure (matches Rust implementation):
    ///     0. Instruct (optional): im_start user \n {instruct} im_end \n — pure text embeddings
    ///     1. Role: im_start assistant \n — pure text embeddings
    ///     2. Control: marker + codec[think/lang] — fused
    ///     3. Speaker: marker + speaker_emb — fused
    ///     4. Text block: BOS_TOKEN + all text + EOS_TOKEN, each + codec[PAD] — fused
    ///     5. Activation: marker + codec[BOS] — fused
    /// </summary>
    public (float[] PrefillEmbedding, int PrefillLength) BuildPrefillEmbedding(
        int[] prefixTokenIds,
        int[] textBodyTokenIds,
        int[] codecPrefix,
        float[]? speakerEmbedding,
        int[]? instructTokenIds = null
    )
    {
        // Instruct block: <|im_start|>user\n + instruct tokens + <|im_end|>\n
        var instructPrefix =
            instructTokenIds != null
                ? new[] { Qwen3TextTokenizer.ImStartId, 872, Qwen3TextTokenizer.NewlineId }
                : Array.Empty<int>();
        var instructSuffix =
            instructTokenIds != null
                ? new[] { Qwen3TextTokenizer.ImEndId, Qwen3TextTokenizer.NewlineId }
                : Array.Empty<int>();
        var instructBlockLen =
            instructPrefix.Length + (instructTokenIds?.Length ?? 0) + instructSuffix.Length;

        var roleCount = prefixTokenIds.Length;
        var controlCount = codecPrefix.Length; // think/lang section only
        var speakerCount = 1; // always 1 speaker token
        var textBlockLen = textBodyTokenIds.Length + 2; // BOS_TOKEN + text + EOS_TOKEN
        var activationLen = 1; // marker + codec BOS

        var prefillLen =
            instructBlockLen
            + roleCount
            + controlCount
            + speakerCount
            + textBlockLen
            + activationLen;
        var prefillEmbedding = new float[prefillLen * TalkerDim];

        var offset = 0;

        // 0. Instruct block (optional): pure text embeddings, no codec fusion
        //    <|im_start|>user\n{instruct_tokens}<|im_end|>\n
        foreach (var id in instructPrefix)
        {
            GetTextEmbedding(id).CopyTo(prefillEmbedding.AsSpan(offset, TalkerDim));
            offset += TalkerDim;
        }

        if (instructTokenIds != null)
        {
            foreach (var id in instructTokenIds)
            {
                GetTextEmbedding(id).CopyTo(prefillEmbedding.AsSpan(offset, TalkerDim));
                offset += TalkerDim;
            }
        }

        foreach (var id in instructSuffix)
        {
            GetTextEmbedding(id).CopyTo(prefillEmbedding.AsSpan(offset, TalkerDim));
            offset += TalkerDim;
        }

        // 1. Role embeddings: raw text embeddings, no codec fusion
        for (var i = 0; i < roleCount; i++)
        {
            GetTextEmbedding(prefixTokenIds[i]).CopyTo(prefillEmbedding.AsSpan(offset, TalkerDim));
            offset += TalkerDim;
        }

        // 2. Control block: text_embd[marker(151671)] + codec_embd[0][code]
        //    think/nothink, think_bos, (lang), think_eos
        for (var i = 0; i < controlCount; i++)
        {
            var dst = prefillEmbedding.AsSpan(offset, TalkerDim);
            _ttsPadEmbedding.AsSpan().CopyTo(dst); // marker = text_embd[151671]
            TensorPrimitives.Add(dst, GetCodecEmbedding(0, codecPrefix[i]), dst);
            offset += TalkerDim;
        }

        // 3. Speaker: text_embd[marker] + speaker_embedding (or codec_embd[0][pad] if no speaker)
        {
            var dst = prefillEmbedding.AsSpan(offset, TalkerDim);
            _ttsPadEmbedding.AsSpan().CopyTo(dst);
            if (speakerEmbedding != null)
            {
                TensorPrimitives.Add(dst, speakerEmbedding.AsSpan(0, TalkerDim), dst);
            }
            else
            {
                TensorPrimitives.Add(dst, GetCodecEmbedding(0, _config.CodecPadId), dst);
            }

            offset += TalkerDim;
        }

        // 4. Text block: each token fused with codec_embd[0][PAD]
        //    Sequence: BOS_TOKEN, text[0], text[1], ..., text[N-1], EOS_TOKEN
        var codecPadEmb = GetCodecEmbedding(0, _config.CodecPadId);

        // BOS_TOKEN (151672) + PAD
        {
            var dst = prefillEmbedding.AsSpan(offset, TalkerDim);
            GetTextEmbedding(_config.TtsBosId).CopyTo(dst);
            TensorPrimitives.Add(dst, codecPadEmb, dst);
            offset += TalkerDim;
        }

        // All text body tokens + PAD
        for (var i = 0; i < textBodyTokenIds.Length; i++)
        {
            var dst = prefillEmbedding.AsSpan(offset, TalkerDim);
            GetTextEmbedding(textBodyTokenIds[i]).CopyTo(dst);
            TensorPrimitives.Add(dst, codecPadEmb, dst);
            offset += TalkerDim;
        }

        // EOS_TOKEN (151673) + PAD
        {
            var dst = prefillEmbedding.AsSpan(offset, TalkerDim);
            GetTextEmbedding(_config.TtsEosId).CopyTo(dst);
            TensorPrimitives.Add(dst, codecPadEmb, dst);
            offset += TalkerDim;
        }

        // 4. Activation: text_embd[marker] + codec_embd[0][BOS]
        {
            var dst = prefillEmbedding.AsSpan(offset, TalkerDim);
            _ttsPadEmbedding.AsSpan().CopyTo(dst);
            TensorPrimitives.Add(dst, GetCodecEmbedding(0, _config.CodecBosId), dst);
        }

        return (prefillEmbedding, prefillLen);
    }

    /// <summary>
    ///     Builds the feedback embedding for the next Talker decode step.
    ///     feedback = sum(codec_embd[q][codes[q]] for q in 0..16) + text_embd[tts_pad]
    ///     This follows the Rust implementation exactly.
    /// </summary>
    public void BuildFeedbackEmbedding(int[] codes, Span<float> output2048)
    {
        output2048.Clear();

        // Sum all 16 codec embeddings (2048-dim each)
        for (var q = 0; q < codes.Length; q++)
        {
            TensorPrimitives.Add(
                output2048[..TalkerDim],
                GetCodecEmbedding(q, codes[q]),
                output2048[..TalkerDim]
            );
        }

        // Add tts_pad text embedding
        TensorPrimitives.Add(
            output2048[..TalkerDim],
            _ttsPadEmbedding.AsSpan(),
            output2048[..TalkerDim]
        );
    }
}
