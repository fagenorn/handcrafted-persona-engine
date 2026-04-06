using System.Diagnostics;
using System.Runtime.InteropServices;
using LLama;
using LLama.Common;
using LLama.Native;
using Microsoft.Extensions.Logging;

namespace PersonaEngine.Lib.TTS.Synthesis.Qwen3;

/// <summary>
///     LLamaSharp-based inference backend for Qwen3-TTS Talker and Code Predictor.
///     Uses GGUF models via llama.cpp for near-zero per-call overhead.
///     The Talker uses 4D RoPE positions via raw P/Invoke; the CP uses managed API.
/// </summary>
internal sealed class LlamaTtsInference : IDisposable
{
    // 4D RoPE: Qwen3 Talker expects [temporal, height, width, channel] per token
    private const int RopeSections = 4;

    private readonly LLamaWeights _talkerModel;
    private readonly ModelParams _talkerParams;
    private LLamaContext _talkerCtx;
    private readonly int _talkerEmbdDim;
    private readonly int _talkerVocabSize;

    private readonly LLamaWeights _predictorModel;
    private LLamaContext _predictorCtx;
    private readonly int _predictorEmbdDim;
    private readonly int _predictorVocabSize;

    // Raw batch for Talker (4D positions require direct memory access)
    private LLamaNativeBatch _talkerBatch;
    private readonly int _talkerBatchCapacity;

    private readonly ILogger? _logger;
    private bool _disposed;

    private LlamaTtsInference(
        LLamaWeights talkerModel,
        ModelParams talkerParams,
        LLamaContext talkerCtx,
        LLamaWeights predictorModel,
        LLamaContext predictorCtx,
        int talkerBatchCapacity,
        ILogger? logger
    )
    {
        _talkerModel = talkerModel;
        _talkerParams = talkerParams;
        _talkerCtx = talkerCtx;
        _talkerEmbdDim = talkerModel.NativeHandle.EmbeddingSize;
        _talkerVocabSize = talkerModel.NativeHandle.Vocab.Count;

        _predictorModel = predictorModel;
        _predictorCtx = predictorCtx;
        _predictorEmbdDim = predictorModel.NativeHandle.EmbeddingSize;
        _predictorVocabSize = predictorModel.NativeHandle.Vocab.Count;

        _talkerBatchCapacity = talkerBatchCapacity;
        _talkerBatch = NativeApi.llama_batch_init(talkerBatchCapacity, _talkerEmbdDim, 1);

        _logger = logger;
    }

    public int TalkerEmbdDim => _talkerEmbdDim;

    public int TalkerVocabSize => _talkerVocabSize;

    public int PredictorEmbdDim => _predictorEmbdDim;

    public int PredictorVocabSize => _predictorVocabSize;

    public static LlamaTtsInference Load(
        string talkerGgufPath,
        string predictorGgufPath,
        ILogger? logger = null
    )
    {
        var talkerParams = new ModelParams(talkerGgufPath)
        {
            GpuLayerCount = 99,
            ContextSize = 4096,
            BatchSize = 2048,
            UBatchSize = 512,
            Embeddings = true,
            FlashAttention = true,
        };

        logger?.LogInformation("Loading Talker GGUF: {Path}", talkerGgufPath);
        var talkerModel = LLamaWeights.LoadFromFile(talkerParams);
        var talkerCtx = talkerModel.CreateContext(talkerParams);
        logger?.LogInformation(
            "Talker: n_embd={EmbdDim}, n_vocab={VocabSize}",
            talkerModel.NativeHandle.EmbeddingSize,
            talkerModel.NativeHandle.Vocab.Count
        );

        var predictorParams = new ModelParams(predictorGgufPath)
        {
            GpuLayerCount = 99,
            ContextSize = 512,
            BatchSize = 32,
            UBatchSize = 32,
            Embeddings = false,
            FlashAttention = true,
        };

        logger?.LogInformation("Loading Predictor GGUF: {Path}", predictorGgufPath);
        var predictorModel = LLamaWeights.LoadFromFile(predictorParams);
        var predictorCtx = predictorModel.CreateContext(predictorParams);
        logger?.LogInformation(
            "Predictor: n_embd={EmbdDim}, n_vocab={VocabSize}",
            predictorModel.NativeHandle.EmbeddingSize,
            predictorModel.NativeHandle.Vocab.Count
        );

        // Batch capacity: must fit 4 * max_tokens positions for 4D RoPE.
        // 2048 slots = up to 512 tokens with 4D positions.
        var batchCapacity = 2048;

        return new LlamaTtsInference(
            talkerModel,
            talkerParams,
            talkerCtx,
            predictorModel,
            predictorCtx,
            batchCapacity,
            logger
        );
    }

    /// <summary>
    ///     Runs Talker prefill: feeds all embeddings with 4D positions.
    ///     Returns logits + hidden state at the last position.
    ///     If llama_decode fails (e.g. stale CUDA state), the context is
    ///     recreated and the prefill retried once as a safety net.
    /// </summary>
    public (float[] Logits, float[] Hidden) TalkerPrefill(float[] embeddings, int numTokens)
    {
        ClearTalkerKvCache();

        SetTalkerBatch(embeddings, numTokens, startPos: 0, logitsAtLast: true);

        var ctxPtr = _talkerCtx.NativeHandle.DangerousGetHandle();
        var result = llama_decode(ctxPtr, _talkerBatch);
        if (result != 0)
        {
            _logger?.LogWarning(
                "Talker prefill failed (code {Result}), recreating context and retrying",
                result
            );
            RecreateTalkerContext();

            ClearTalkerKvCache();
            SetTalkerBatch(embeddings, numTokens, startPos: 0, logitsAtLast: true);
            ctxPtr = _talkerCtx.NativeHandle.DangerousGetHandle();
            result = llama_decode(ctxPtr, _talkerBatch);
            if (result != 0)
            {
                throw new InvalidOperationException(
                    $"Talker prefill failed after context recovery: llama_decode returned {result}"
                );
            }
        }

        var logits = _talkerCtx.NativeHandle.GetLogitsIth(numTokens - 1).ToArray();
        var hidden = _talkerCtx.NativeHandle.GetEmbeddingsIth(numTokens - 1).ToArray();

        return (logits, hidden);
    }

    /// <summary>
    ///     Runs a single Talker decode step with one embedding at the given position.
    ///     KV cache is preserved from the previous call (incremental decoding).
    /// </summary>
    public (float[] Logits, float[] Hidden) TalkerDecode(float[] embedding, int position)
    {
        _logger?.LogTrace("TalkerDecode: starting at position {Position}", position);
        var sw = Stopwatch.StartNew();

        SetTalkerBatch(embedding, numTokens: 1, startPos: position, logitsAtLast: true);

        var ctxPtr = _talkerCtx.NativeHandle.DangerousGetHandle();
        var result = llama_decode(ctxPtr, _talkerBatch);
        var decodeMs = sw.ElapsedMilliseconds;

        if (result != 0)
        {
            _logger?.LogError(
                "TalkerDecode: llama_decode returned {Result} at position {Position} after {Elapsed}ms",
                result,
                position,
                decodeMs
            );

            throw new InvalidOperationException(
                $"Talker decode failed at position {position}: llama_decode returned {result}"
            );
        }

        if (decodeMs > 2000)
        {
            _logger?.LogWarning(
                "TalkerDecode: slow decode at position {Position} took {Elapsed}ms",
                position,
                decodeMs
            );
        }

        var logits = _talkerCtx.NativeHandle.GetLogitsIth(0).ToArray();
        var hidden = _talkerCtx.NativeHandle.GetEmbeddingsIth(0).ToArray();

        _logger?.LogTrace(
            "TalkerDecode: position {Position} completed in {Elapsed}ms",
            position,
            sw.ElapsedMilliseconds
        );

        return (logits, hidden);
    }

    /// <summary>
    ///     Destroys and recreates the Talker CUDA context + batch.
    ///     Recovers from CUDA graph capture failures that corrupt the GPU state
    ///     after repeated prefill/decode cycles with varying sequence lengths.
    /// </summary>
    private void RecreateTalkerContext()
    {
        _logger?.LogInformation("Recreating Talker CUDA context...");

        NativeApi.llama_batch_free(_talkerBatch);
        _talkerCtx.Dispose();

        _talkerCtx = _talkerModel.CreateContext(_talkerParams);
        _talkerBatch = NativeApi.llama_batch_init(_talkerBatchCapacity, _talkerEmbdDim, 1);

        _logger?.LogInformation("Talker CUDA context recreated successfully");
    }

    /// <summary>
    ///     Runs the Code Predictor for groups 1-15 using the managed LLamaBatchEmbeddings API.
    ///     Clears CP KV-cache before each frame, then autoregressively generates 15 codes.
    ///     The Predictor's vocabulary is partitioned: group g uses logits[(g-1)*2048 .. g*2048].
    /// </summary>
    public void PredictCodes(
        float[] projectedHidden,
        int[] codes,
        int numGroups,
        float temperature,
        int topK,
        bool greedy,
        Func<int, int, float[]> getCodecEmbedding1024
    )
    {
        const int codesPerGroup = 2048;
        var sw = Stopwatch.StartNew();

        _predictorCtx.NativeHandle.MemoryClear();

        var batch = new LLamaBatchEmbeddings(_predictorEmbdDim);

        // Step 1: feed projected hidden (pos 0) + code_0 embedding (pos 1)
        var code0Emb = getCodecEmbedding1024(0, codes[0]);
        batch.Add(projectedHidden.AsSpan(0, _predictorEmbdDim), 0, (LLamaSeqId)0, false);
        batch.Add(code0Emb.AsSpan(0, _predictorEmbdDim), 1, (LLamaSeqId)0, true);

        var decodeResult = _predictorCtx.NativeHandle.Decode(batch);
        if (decodeResult != DecodeResult.Ok)
        {
            _logger?.LogError(
                "PredictCodes: initial decode failed with {Result} for code_0={Code0} after {Elapsed}ms",
                decodeResult,
                codes[0],
                sw.ElapsedMilliseconds
            );

            throw new InvalidOperationException($"Predictor decode failed: {decodeResult}");
        }

        // Sample code_1 from logits slice [0*2048 .. 1*2048]
        var logits1 = _predictorCtx.NativeHandle.GetLogitsIth(1);
        var rawToken1 = Qwen3Sampler.SampleCodePredictorToken(
            logits1.Slice(0, codesPerGroup),
            codesPerGroup,
            temperature,
            topK,
            greedy
        );
        codes[1] = rawToken1;

        // Steps 2-15: one embedding each
        for (var g = 2; g < numGroups; g++)
        {
            batch.Clear();
            var prevCodeEmb = getCodecEmbedding1024(g - 1, codes[g - 1]);
            batch.Add(prevCodeEmb.AsSpan(0, _predictorEmbdDim), g, (LLamaSeqId)0, true);

            decodeResult = _predictorCtx.NativeHandle.Decode(batch);
            if (decodeResult != DecodeResult.Ok)
            {
                _logger?.LogError(
                    "PredictCodes: group {Group} decode failed with {Result} after {Elapsed}ms",
                    g,
                    decodeResult,
                    sw.ElapsedMilliseconds
                );

                throw new InvalidOperationException(
                    $"Predictor decode step {g} failed: {decodeResult}"
                );
            }

            // Logit slicing: group g (1-indexed) uses offset (g-1)*2048
            var logitsG = _predictorCtx.NativeHandle.GetLogitsIth(0);
            var sliceOffset = (g - 1) * codesPerGroup;
            var rawTokenG = Qwen3Sampler.SampleCodePredictorToken(
                logitsG.Slice(sliceOffset, codesPerGroup),
                codesPerGroup,
                temperature,
                topK,
                greedy
            );
            codes[g] = rawTokenG;
        }

        var totalMs = sw.ElapsedMilliseconds;
        if (totalMs > 2000)
        {
            _logger?.LogWarning(
                "PredictCodes: slow prediction took {Elapsed}ms for {Groups} groups",
                totalMs,
                numGroups
            );
        }

        _logger?.LogTrace(
            "PredictCodes: completed {Groups} groups in {Elapsed}ms",
            numGroups,
            totalMs
        );
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        NativeApi.llama_batch_free(_talkerBatch);
        _talkerCtx.Dispose();
        _talkerModel.Dispose();
        _predictorCtx.Dispose();
        _predictorModel.Dispose();
        _disposed = true;
    }

    /// <summary>
    ///     Sets up the raw Talker batch with embeddings and 4D RoPE positions.
    ///     Position layout in memory: [temporal_0..n, height_0..n, width_0..n, channel_0..n]
    ///     where temporal=height=width=position, channel=0.
    /// </summary>
    private unsafe void SetTalkerBatch(
        float[] embeddings,
        int numTokens,
        int startPos,
        bool logitsAtLast
    )
    {
        if (numTokens * RopeSections > _talkerBatchCapacity)
        {
            throw new ArgumentException(
                $"Batch capacity {_talkerBatchCapacity} too small for {numTokens} tokens "
                    + $"with 4D positions ({numTokens * RopeSections} needed)"
            );
        }

        _talkerBatch.n_tokens = numTokens;

        // Copy embeddings: numTokens * embdDim floats
        var embdLen = numTokens * _talkerEmbdDim;
        fixed (float* src = embeddings)
        {
            Buffer.MemoryCopy(
                src,
                _talkerBatch.embd,
                embdLen * sizeof(float),
                embdLen * sizeof(float)
            );
        }

        // Set 4D positions: [temporal, height, width, channel]
        // llama.cpp reads n_tokens * rope_sections positions from this buffer
        var pos = (int*)_talkerBatch.pos;
        for (var i = 0; i < numTokens; i++)
        {
            pos[i] = startPos + i; // temporal
            pos[numTokens + i] = startPos + i; // height
            pos[2 * numTokens + i] = startPos + i; // width
            pos[3 * numTokens + i] = 0; // channel (always 0)
        }

        // Set seq_id, n_seq_id, logits
        for (var i = 0; i < numTokens; i++)
        {
            _talkerBatch.n_seq_id[i] = 1;
            _talkerBatch.seq_id[i][0] = (LLamaSeqId)0;
            _talkerBatch.logits[i] = (byte)(logitsAtLast && i == numTokens - 1 ? 1 : 0);
        }
    }

    private void ClearTalkerKvCache()
    {
        _talkerCtx.NativeHandle.MemoryClear();
    }

    // P/Invoke for llama_decode with raw batch — needed because
    // SafeLLamaContextHandle.Decode only accepts managed batch types,
    // and we need raw batch for 4D RoPE position control.

    [DllImport("llama", CallingConvention = CallingConvention.Cdecl)]
    private static extern int llama_decode(IntPtr ctx, LLamaNativeBatch batch);
}
