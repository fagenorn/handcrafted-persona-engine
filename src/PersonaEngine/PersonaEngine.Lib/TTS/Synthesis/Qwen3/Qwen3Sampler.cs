using System.Runtime.CompilerServices;
using PersonaEngine.Lib.Utils;

namespace PersonaEngine.Lib.TTS.Synthesis.Qwen3;

/// <summary>
///     Token sampling utilities for autoregressive generation.
///     Supports temperature scaling, top-K filtering, repetition penalty, and multinomial sampling.
/// </summary>
internal static class Qwen3Sampler
{
    private static readonly ThreadLocal<Random> Rng = new(() => new Random());

    /// <summary>
    ///     Sliding window size for repetition penalty, matching llama.cpp's penalty_last_n.
    /// </summary>
    public const int RepetitionPenaltyWindow = 128;

    /// <summary>
    ///     Samples a token from logits with full Talker sampling strategy:
    ///     repetition penalty (sliding window) → min_new_tokens suppression → token suppression →
    ///     silence penalty → temperature → top-K → softmax → multinomial.
    /// </summary>
    public static int SampleTalkerToken(
        ReadOnlySpan<float> logits,
        int vocabSize,
        int codecEosTokenId,
        ReadOnlySpan<int> recentTokens,
        Qwen3GenerationOptions options,
        float silencePenalty = 0f,
        int silentThreshold = 0,
        int suppressTokenId = -1
    )
    {
        Span<float> processed = stackalloc float[vocabSize];
        // Take the LAST vocabSize elements (logits for the final position)
        logits.Slice(logits.Length - vocabSize, vocabSize).CopyTo(processed);

        // Repetition penalty (sliding window of last 128 tokens, matching llama.cpp)
        if (options.RepetitionPenalty > 1.0f && recentTokens.Length > 0)
        {
            ApplyRepetitionPenalty(processed, recentTokens, options.RepetitionPenalty);
        }

        // min_new_tokens: suppress a specific token (typically EOS) for early steps
        if (suppressTokenId >= 0 && suppressTokenId < vocabSize)
        {
            processed[suppressTokenId] = float.NegativeInfinity;
        }

        // Suppress control tokens [vocabSize-1024, vocabSize) except codecEosTokenId
        var suppressStart = vocabSize - 1024;
        for (var i = suppressStart; i < vocabSize; i++)
        {
            if (i != codecEosTokenId)
            {
                processed[i] = float.NegativeInfinity;
            }
        }

        // Silence penalty: penalize tokens in [0, silentThreshold) to prevent
        // the model from generating long silent tails at the end of speech.
        if (silencePenalty > 0f && silentThreshold > 0)
        {
            for (var i = 0; i < silentThreshold && i < processed.Length; i++)
            {
                processed[i] -= silencePenalty;
            }
        }

        // Temperature
        if (options.Temperature > 0 && MathF.Abs(options.Temperature - 1.0f) > 1e-6f)
        {
            ApplyTemperature(processed, options.Temperature);
        }

        // Top-K
        if (options.TopK > 0 && options.TopK < vocabSize)
        {
            ApplyTopK(processed, options.TopK);
        }

        // Top-P (nucleus sampling)
        if (options.TopP > 0f && options.TopP < 1.0f)
        {
            ApplyTopP(processed, options.TopP);
        }

        // Softmax + sample
        Softmax(processed);

        return MultinomialSample(processed);
    }

    /// <summary>
    ///     Samples a Code Predictor token (groups 1-15).
    ///     Default mode (greedy=false): temperature + top-K + multinomial, matching
    ///     the official Qwen3-TTS Python implementation and LunaVox.
    ///     Greedy mode: argmax, matching some Rust implementations for cleaner spectral output.
    /// </summary>
    public static int SampleCodePredictorToken(
        ReadOnlySpan<float> logits,
        int vocabSize,
        float temperature,
        int topK,
        bool greedy = false,
        float topP = 1.0f
    )
    {
        var slice = logits.Slice(logits.Length - vocabSize, vocabSize);

        if (greedy)
        {
            return slice.ArgMax();
        }

        Span<float> processed = stackalloc float[vocabSize];
        slice.CopyTo(processed);

        if (temperature > 0 && MathF.Abs(temperature - 1.0f) > 1e-6f)
        {
            ApplyTemperature(processed, temperature);
        }

        if (topK > 0 && topK < vocabSize)
        {
            ApplyTopK(processed, topK);
        }

        if (topP > 0f && topP < 1.0f)
        {
            ApplyTopP(processed, topP);
        }

        Softmax(processed);

        return MultinomialSample(processed);
    }

    /// <summary>
    ///     Applies HuggingFace-style repetition penalty over a sliding window of recent tokens.
    ///     Uses stackalloc for dedup tracking — zero heap allocations. vocabSize ~3072 is safe for stack.
    /// </summary>
    private static void ApplyRepetitionPenalty(
        Span<float> logits,
        ReadOnlySpan<int> recentTokens,
        float penalty
    )
    {
        Span<bool> penalized = stackalloc bool[logits.Length];
        penalized.Clear();

        foreach (var token in recentTokens)
        {
            if ((uint)token < (uint)logits.Length && !penalized[token])
            {
                penalized[token] = true;
                logits[token] =
                    logits[token] > 0 ? logits[token] / penalty : logits[token] * penalty;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyTemperature(Span<float> logits, float temperature)
    {
        for (var i = 0; i < logits.Length; i++)
        {
            logits[i] /= temperature;
        }
    }

    private static void ApplyTopP(Span<float> logits, float p)
    {
        // Compute softmax probabilities into a temporary buffer
        var length = logits.Length;
        Span<float> probs = stackalloc float[length];
        logits.CopyTo(probs);
        Softmax(probs);

        // Build index array and sort by probability descending
        Span<int> indices = stackalloc int[length];
        for (var i = 0; i < length; i++)
        {
            indices[i] = i;
        }

        // Insertion sort by descending probability — vocab is small (~2048/3072)
        for (var i = 1; i < length; i++)
        {
            var key = indices[i];
            var keyProb = probs[key];
            var j = i - 1;
            while (j >= 0 && probs[indices[j]] < keyProb)
            {
                indices[j + 1] = indices[j];
                j--;
            }

            indices[j + 1] = key;
        }

        // Walk sorted tokens until cumulative probability exceeds p, mask the rest
        var cumulative = 0f;
        var cutoff = -1;
        for (var i = 0; i < length; i++)
        {
            cumulative += probs[indices[i]];
            if (cumulative > p)
            {
                cutoff = i;

                break;
            }
        }

        // If all tokens fit within p, nothing to mask
        if (cutoff < 0)
        {
            return;
        }

        // Keep at least one token (the most probable)
        if (cutoff == 0)
        {
            cutoff = 1;
        }

        // Mask everything after the cutoff
        for (var i = cutoff; i < length; i++)
        {
            logits[indices[i]] = float.NegativeInfinity;
        }
    }

    private static void ApplyTopK(Span<float> logits, int k)
    {
        // Find the k-th largest value
        // Use partial sort via selection: collect top-k values
        Span<float> topValues = stackalloc float[k];
        topValues.Fill(float.NegativeInfinity);

        for (var i = 0; i < logits.Length; i++)
        {
            var val = logits[i];
            if (val <= topValues[k - 1])
            {
                continue;
            }

            // Insert into sorted top-k
            var insertIdx = k - 1;
            while (insertIdx > 0 && topValues[insertIdx - 1] < val)
            {
                topValues[insertIdx] = topValues[insertIdx - 1];
                insertIdx--;
            }

            topValues[insertIdx] = val;
        }

        var threshold = topValues[k - 1];

        for (var i = 0; i < logits.Length; i++)
        {
            if (logits[i] < threshold)
            {
                logits[i] = float.NegativeInfinity;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Softmax(Span<float> logits)
    {
        var max = float.NegativeInfinity;
        for (var i = 0; i < logits.Length; i++)
        {
            if (logits[i] > max)
            {
                max = logits[i];
            }
        }

        var sum = 0f;
        for (var i = 0; i < logits.Length; i++)
        {
            logits[i] = MathF.Exp(logits[i] - max);
            sum += logits[i];
        }

        if (sum > 0)
        {
            for (var i = 0; i < logits.Length; i++)
            {
                logits[i] /= sum;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int MultinomialSample(ReadOnlySpan<float> probabilities)
    {
        var r = (float)Rng.Value!.NextDouble();
        var cumulative = 0f;

        for (var i = 0; i < probabilities.Length; i++)
        {
            cumulative += probabilities[i];
            if (r <= cumulative)
            {
                return i;
            }
        }

        // Fallback: return last valid token
        return probabilities.Length - 1;
    }
}
