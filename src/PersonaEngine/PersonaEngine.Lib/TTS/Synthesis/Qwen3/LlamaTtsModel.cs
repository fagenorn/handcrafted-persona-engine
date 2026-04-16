using System.Collections.Concurrent;
using LLama;
using LLama.Common;
using Microsoft.Extensions.Logging;

namespace PersonaEngine.Lib.TTS.Synthesis.Qwen3;

/// <summary>
///     Shared model weights for Qwen3-TTS Talker and Code Predictor.
///     Manages a bounded pool of <see cref="LlamaTtsContext" /> instances
///     so multiple callers can run inference concurrently without racing
///     on the same llama.cpp native contexts.
/// </summary>
internal sealed class LlamaTtsModel : IDisposable
{
    private const int MaxPoolSize = 2;
    private const int TalkerBatchCapacity = 2048;

    private readonly LLamaWeights _talkerModel;
    private readonly ModelParams _talkerParams;
    private readonly LLamaWeights _predictorModel;
    private readonly ModelParams _predictorParams;
    private readonly ILogger? _logger;

    private readonly ConcurrentBag<LlamaTtsContext> _pool = new();
    private readonly SemaphoreSlim _gate = new(MaxPoolSize, MaxPoolSize);
    private bool _disposed;

    private LlamaTtsModel(
        LLamaWeights talkerModel,
        ModelParams talkerParams,
        LLamaWeights predictorModel,
        ModelParams predictorParams,
        ILogger? logger
    )
    {
        _talkerModel = talkerModel;
        _talkerParams = talkerParams;
        _predictorModel = predictorModel;
        _predictorParams = predictorParams;
        _logger = logger;

        TalkerEmbdDim = talkerModel.NativeHandle.EmbeddingSize;
        TalkerVocabSize = talkerModel.NativeHandle.Vocab.Count;
        PredictorEmbdDim = predictorModel.NativeHandle.EmbeddingSize;
        PredictorVocabSize = predictorModel.NativeHandle.Vocab.Count;
    }

    public int TalkerEmbdDim { get; }

    public int TalkerVocabSize { get; }

    public int PredictorEmbdDim { get; }

    public int PredictorVocabSize { get; }

    public static LlamaTtsModel Load(
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
        logger?.LogInformation(
            "Predictor: n_embd={EmbdDim}, n_vocab={VocabSize}",
            predictorModel.NativeHandle.EmbeddingSize,
            predictorModel.NativeHandle.Vocab.Count
        );

        return new LlamaTtsModel(
            talkerModel,
            talkerParams,
            predictorModel,
            predictorParams,
            logger
        );
    }

    /// <summary>
    ///     Rents an inference context from the pool, creating one if the pool is empty.
    ///     Blocks if the maximum number of contexts are already checked out.
    /// </summary>
    public LlamaTtsContext RentContext()
    {
        _gate.Wait();

        if (_pool.TryTake(out var ctx))
        {
            _logger?.LogDebug("LlamaTtsModel: reusing pooled context");
            return ctx;
        }

        _logger?.LogInformation("LlamaTtsModel: creating new inference context");
        return new LlamaTtsContext(
            _talkerModel,
            _talkerParams,
            _predictorModel,
            _predictorParams,
            TalkerBatchCapacity,
            _logger
        );
    }

    /// <summary>
    ///     Returns an inference context to the pool.
    ///     If the context is corrupted (e.g. after a CUDA failure), it is disposed
    ///     instead of being returned — the next <see cref="RentContext" /> call will
    ///     create a fresh one.
    /// </summary>
    public void ReturnContext(LlamaTtsContext context, bool corrupted = false)
    {
        if (corrupted)
        {
            _logger?.LogWarning("LlamaTtsModel: disposing corrupted context");
            context.Dispose();
        }
        else
        {
            context.Reset();
            _pool.Add(context);
        }

        _gate.Release();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        while (_pool.TryTake(out var ctx))
        {
            ctx.Dispose();
        }

        _talkerModel.Dispose();
        _predictorModel.Dispose();
        _gate.Dispose();
    }
}
