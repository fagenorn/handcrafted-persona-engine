using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using PersonaEngine.Lib.IO;
using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.TTS.Synthesis.Alignment;
using PersonaEngine.Lib.Utils.Onnx;

namespace PersonaEngine.Lib.TTS.Synthesis.Qwen3;

/// <summary>
///     Qwen3-TTS inference engine using GGUF models via LLamaSharp (llama.cpp).
///     Talker + Code Predictor run through llama.cpp for near-zero per-call overhead.
///     Audio decoder uses ONNX Runtime (streaming stateful decoder).
///     Output: 24 kHz mono float32 PCM.
/// </summary>
public sealed class Qwen3TtsGgufEngine : IDisposable
{
    public const int OutputSampleRate = 24000;
    private const int DefaultMaxNewTokens = 2048;
    private const int SamplesPerFrame = 1920;
    private const int TalkerDim = 2048;

    /// <summary>
    ///     Number of samples for the fade-out applied to the final audio chunk
    ///     to prevent clicks/pops from conv buffer flush tails. 30ms at 24 kHz.
    /// </summary>
    private const int FadeOutSamples = 720;

    private readonly Qwen3ModelConfig _config;
    private readonly Qwen3TextTokenizer _tokenizer;
    private readonly GgufEmbeddingManager _embeddings;
    private readonly LlamaTtsInference _llama;
    private readonly InferenceSession _decoderSession;
    private readonly IForcedAligner _aligner;
    private readonly ILogger? _logger;

    private bool _disposed;

    private Qwen3TtsGgufEngine(
        Qwen3ModelConfig config,
        Qwen3TextTokenizer tokenizer,
        GgufEmbeddingManager embeddings,
        LlamaTtsInference llama,
        InferenceSession decoderSession,
        IForcedAligner aligner,
        ILogger? logger
    )
    {
        _config = config;
        _tokenizer = tokenizer;
        _embeddings = embeddings;
        _llama = llama;
        _decoderSession = decoderSession;
        _aligner = aligner;
        _logger = logger;
    }

    /// <summary>
    ///     Loads the GGUF engine. All model paths are resolved via <see cref="IModelProvider" />.
    /// </summary>
    public static Qwen3TtsGgufEngine Load(
        IModelProvider modelProvider,
        IForcedAligner aligner,
        ILogger? logger = null
    )
    {
        var sw = Stopwatch.StartNew();

        var config = Qwen3ModelConfig.Load(modelProvider.GetModelPath(IO.ModelType.Qwen3.Config));
        var tokenizer = Qwen3TextTokenizer.Load(
            modelProvider.GetModelPath(IO.ModelType.Qwen3.Tokenizer)
        );
        var embeddings = GgufEmbeddingManager.Load(
            modelProvider.GetModelPath(IO.ModelType.Qwen3.Embeddings),
            modelProvider.GetModelPath(IO.ModelType.Qwen3.Speakers),
            config
        );

        logger?.LogInformation(
            "Loaded config, tokenizer, embeddings in {Elapsed}ms",
            sw.ElapsedMilliseconds
        );

        // Load GGUF models via LLamaSharp
        var llama = LlamaTtsInference.Load(
            modelProvider.GetModelPath(IO.ModelType.Qwen3.Talker),
            modelProvider.GetModelPath(IO.ModelType.Qwen3.Predictor),
            logger
        );

        logger?.LogInformation("LLamaSharp models loaded in {Elapsed}ms", sw.ElapsedMilliseconds);

        // Load streaming audio decoder (ONNX, GPU-accelerated)
        var decoderSession = OnnxSessionFactory.Create(
            modelProvider.GetModelPath(IO.ModelType.Qwen3.Decoder),
            ExecutionProvider.Cuda,
            SessionProfile.Sequential
        );

        logger?.LogInformation("All models loaded in {Elapsed}ms total", sw.ElapsedMilliseconds);

        var engine = new Qwen3TtsGgufEngine(
            config,
            tokenizer,
            embeddings,
            llama,
            decoderSession,
            aligner,
            logger
        );

        // Warmup: run a minimal prefill to trigger GGUF JIT/kernel caching
        var warmupTokens = tokenizer.Encode("Hi");
        var warmupPrefix = new[]
        {
            Qwen3TextTokenizer.ImStartId,
            Qwen3TextTokenizer.AssistantId,
            Qwen3TextTokenizer.NewlineId,
        };
        var warmupCodecPrefix = embeddings.BuildCodecPrefix("english");
        var (warmupEmb, warmupLen) = embeddings.BuildPrefillEmbedding(
            warmupPrefix,
            warmupTokens,
            warmupCodecPrefix,
            null
        );
        llama.TalkerPrefill(warmupEmb, warmupLen);
        logger?.LogInformation("Warmup complete in {Elapsed}ms total", sw.ElapsedMilliseconds);

        return engine;
    }

    /// <summary>
    ///     Creates a new streaming audio decoder that can be reused across multiple
    ///     <see cref="GenerateStreaming" /> calls for cross-sentence continuity.
    ///     The caller owns the decoder's lifetime.
    /// </summary>
    public Qwen3StreamingAudioDecoder CreateAudioDecoder() => new(_decoderSession, _logger);

    /// <summary>
    ///     Generates speech audio in streaming chunks.
    /// </summary>
    public async IAsyncEnumerable<float[]> GenerateStreaming(
        Qwen3StreamingAudioDecoder decoder,
        string text,
        bool isLastSegment,
        string speaker = "ryan",
        string language = "english",
        string? instruct = null,
        Qwen3GenerationOptions? options = null,
        int emitEveryFrames = 4,
        List<float>? entropyAccumulator = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        options ??= new Qwen3GenerationOptions();
        var sw = Stopwatch.StartNew();

        var (prefix, textBody) = _tokenizer.BuildCustomVoicePrompt(text);
        var codecPrefix = _embeddings.BuildCodecPrefix(language);
        var speakerEmb = _embeddings.GetSpeakerEmbedding(speaker);
        var instructTokens = instruct != null ? _tokenizer.Encode(instruct) : null;

        var (prefillEmbedding, prefillLen) = _embeddings.BuildPrefillEmbedding(
            prefix,
            textBody,
            codecPrefix,
            speakerEmb,
            instructTokens
        );

        var (logits, hidden) = _llama.TalkerPrefill(prefillEmbedding, prefillLen);

        _logger?.LogInformation("Streaming: prefill done in {Elapsed}ms", sw.ElapsedMilliseconds);

        var audioChannel = Channel.CreateBounded<float[]>(
            new BoundedChannelOptions(2) { SingleWriter = true, SingleReader = true }
        );

        var allCodes = new List<int[]>();

        var producerTask = Task.Run(
            async () =>
            {
                var emittedFrames = 0;
                Task<float[]>? pendingDecode = null;
                var pendingEmittedTo = 0;
                var producerSw = Stopwatch.StartNew();
                var lastProgressLog = 0L;

                _logger?.LogDebug(
                    "Streaming producer: starting frame generation for text ({TextLen} chars)",
                    text.Length
                );

                try
                {
                    foreach (
                        var codes in GenerateCodeFrames(
                            logits,
                            hidden,
                            prefillLen,
                            options,
                            entropyAccumulator
                        )
                    )
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        allCodes.Add(codes);

                        // Log a warning if no audio has been emitted for 30s
                        var elapsed = producerSw.ElapsedMilliseconds;
                        if (elapsed - lastProgressLog > 30000)
                        {
                            _logger?.LogWarning(
                                "Streaming producer: {Frames} codec frames generated, {Emitted} emitted as audio, {Elapsed}ms elapsed — may be stuck",
                                allCodes.Count,
                                emittedFrames,
                                elapsed
                            );
                            lastProgressLog = elapsed;
                        }

                        // Always hold back 1 frame so the post-loop code always has
                        // at least 1 real frame for the isLast=true decode. This avoids
                        // needing a synthetic pad-code frame to flush conv buffers.
                        var available = allCodes.Count - emittedFrames - 1;
                        if (available < emitEveryFrames)
                        {
                            continue;
                        }

                        if (pendingDecode != null)
                        {
                            var chunk = await pendingDecode.ConfigureAwait(false);
                            if (chunk.Length > 0)
                            {
                                await audioChannel
                                    .Writer.WriteAsync(chunk, cancellationToken)
                                    .ConfigureAwait(false);
                            }

                            emittedFrames = pendingEmittedTo;
                            pendingDecode = null;
                        }

                        available = allCodes.Count - emittedFrames - 1;
                        if (available < emitEveryFrames)
                        {
                            continue;
                        }

                        // Emit all but the last frame
                        var batchCount = allCodes.Count - emittedFrames - 1;
                        var batch = allCodes.GetRange(emittedFrames, batchCount);
                        pendingDecode = Task.Run(
                            () => decoder.Decode(batch, isLast: false),
                            cancellationToken
                        );
                        pendingEmittedTo = emittedFrames + batchCount;
                    }

                    if (pendingDecode != null)
                    {
                        var chunk = await pendingDecode.ConfigureAwait(false);
                        if (chunk.Length > 0)
                        {
                            await audioChannel
                                .Writer.WriteAsync(chunk, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        emittedFrames = pendingEmittedTo;
                    }

                    // Remaining always has >= 1 frame (the held-back frame)
                    var remaining = allCodes.GetRange(
                        emittedFrames,
                        allCodes.Count - emittedFrames
                    );

                    if (isLastSegment)
                    {
                        // Final segment: decode remaining with conv buffer flush
                        if (remaining.Count > 0)
                        {
                            var finalChunk = decoder.Decode(remaining, isLast: true);
                            if (finalChunk.Length > 0)
                            {
                                ApplyFadeOut(finalChunk);
                                await audioChannel
                                    .Writer.WriteAsync(finalChunk, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                        }
                    }
                    else if (remaining.Count > 0)
                    {
                        // Non-final segment: decode remaining frames without flushing
                        var chunk = decoder.Decode(remaining, isLast: false);
                        if (chunk.Length > 0)
                        {
                            await audioChannel
                                .Writer.WriteAsync(chunk, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger?.LogDebug(
                        "Streaming producer: cancelled after {Frames} frames, {Elapsed}ms",
                        allCodes.Count,
                        producerSw.ElapsedMilliseconds
                    );

                    throw;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(
                        ex,
                        "Streaming producer: error after {Frames} frames, {Emitted} emitted, {Elapsed}ms",
                        allCodes.Count,
                        emittedFrames,
                        producerSw.ElapsedMilliseconds
                    );

                    throw;
                }
                finally
                {
                    // Await any in-flight decoder task before completing the channel.
                    // Without this, the decoder could be disposed (via session dispose)
                    // while still running ONNX inference on the GPU, corrupting CUDA state.
                    if (pendingDecode != null)
                    {
                        try
                        {
                            await pendingDecode.ConfigureAwait(false);
                        }
                        catch
                        {
                            // Swallow — we're cleaning up, the result is discarded
                        }
                    }

                    audioChannel.Writer.Complete();
                }
            },
            cancellationToken
        );

        try
        {
            await foreach (
                var chunk in audioChannel
                    .Reader.ReadAllAsync(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                yield return chunk;
            }

            await producerTask.ConfigureAwait(false);
        }
        finally
        {
            // Always await the producer task to ensure the GPU is idle before
            // the caller can start a new generation (TalkerPrefill/MemoryClear).
            // Without this, cancellation leaves an orphaned producer still calling
            // TalkerDecode, and the next turn's TalkerPrefill races with it,
            // corrupting CUDA state ("operation failed due to a previous error").
            try
            {
                await producerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Producer task failed during cancellation cleanup");
            }
        }

        _logger?.LogInformation(
            "Streaming complete: {Frames} frames, {Duration:F2}s audio in {Elapsed}ms",
            allCodes.Count,
            allCodes.Count * SamplesPerFrame / (float)OutputSampleRate,
            sw.ElapsedMilliseconds
        );
    }

    /// <summary>
    ///     Generates audio with real-time progressive word timing for subtitle highlighting.
    ///     Strategy:
    ///       1. Every audio chunk carries the full sentence's tokens with best-known timing
    ///       2. Every ~0.5s, AlignSpoken updates timing for confirmed words
    ///       3. Token timing is adjusted to chunk-relative so SubtitleProcessor computes
    ///          correct absolute times (segmentStart + relativeOffset = absoluteTime)
    ///       4. No empty-audio timing segments — every segment has audio
    /// </summary>
    public async IAsyncEnumerable<AudioSegment> GenerateStreamingWithTimings(
        Qwen3StreamingAudioDecoder decoder,
        string text,
        PhonemeResult phonemeResult,
        bool isLastSegment,
        string speaker = "ryan",
        string language = "english",
        string? instruct = null,
        Qwen3GenerationOptions? options = null,
        int emitEveryFrames = 4,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var tokens = phonemeResult.Tokens;

        // Build CTC alignment text from phonemizer tokens
        var alignText = string.Concat(tokens.Select(t => t.Text + t.Whitespace)).TrimEnd();

        // CTC aligner operates on space-separated words, which may differ from
        // phonemizer tokens (e.g. punctuation tokens, contractions split into sub-tokens).
        // Build a mapping: CTC word index → range of phonemizer token indices.
        var ctcWords = alignText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wordToTokenRange = BuildWordToTokenMapping(tokens, ctcWords);

        var absoluteTimings = new (double Start, double End)?[ctcWords.Length];

        var accumulatedAudio = new List<float>(OutputSampleRate);
        var totalSamplesEmitted = 0;
        var lastAlignAt = 0;
        var confirmedWordCount = 0;
        var windowStartSample = 0;
        const int alignInterval = OutputSampleRate / 2;
        const int windowOverlapSamples = OutputSampleRate / 5;

        var lastEmittedSample = 0;
        var lastEmittedWordCount = 0;

        await foreach (
            var audioChunk in GenerateStreaming(
                decoder,
                text,
                isLastSegment,
                speaker,
                language,
                instruct,
                options,
                emitEveryFrames,
                cancellationToken: cancellationToken
            )
        )
        {
            accumulatedAudio.AddRange(audioChunk);
            totalSamplesEmitted += audioChunk.Length;

            if (totalSamplesEmitted - lastAlignAt < alignInterval)
            {
                continue;
            }

            if (confirmedWordCount < ctcWords.Length)
            {
                var remainingText = string.Join(' ', ctcWords.AsSpan(confirmedWordCount).ToArray());

                var winStart = Math.Max(0, windowStartSample - windowOverlapSamples);
                var winLength = totalSamplesEmitted - winStart;
                var windowStartTimeSec = winStart / (double)OutputSampleRate;

                var allAudio = CollectionsMarshal.AsSpan(accumulatedAudio);
                var audioWindow = allAudio.Slice(winStart, winLength);

                using var result = _aligner.AlignSpokenWindowed(
                    audioWindow,
                    remainingText,
                    OutputSampleRate,
                    windowStartTimeSec
                );

                if (result.Count > 0)
                {
                    for (var i = 0; i < result.Count; i++)
                    {
                        var wordIdx = confirmedWordCount + i;
                        if (wordIdx < ctcWords.Length)
                        {
                            absoluteTimings[wordIdx] = (
                                result.Timings[i].StartTime.TotalSeconds,
                                result.Timings[i].EndTime.TotalSeconds
                            );
                        }
                    }

                    confirmedWordCount += result.Count;

                    var lastConfirmedEnd = absoluteTimings[confirmedWordCount - 1]!.Value.End;
                    windowStartSample = (int)(lastConfirmedEnd * OutputSampleRate);
                }
            }

            lastAlignAt = totalSamplesEmitted;

            for (var w = lastEmittedWordCount; w < confirmedWordCount; w++)
            {
                var wordEndSample = Math.Min(
                    (int)(absoluteTimings[w]!.Value.End * OutputSampleRate),
                    totalSamplesEmitted
                );

                if (wordEndSample > lastEmittedSample)
                {
                    var sliceStartSec = lastEmittedSample / (double)OutputSampleRate;
                    var emitLength = wordEndSample - lastEmittedSample;
                    var audioToEmit = CollectionsMarshal
                        .AsSpan(accumulatedAudio)
                        .Slice(lastEmittedSample, emitLength)
                        .ToArray();

                    var (tokStart, tokCount) = wordToTokenRange[w];
                    TokenTimingUtils.DistributeTimings(
                        tokens,
                        tokStart,
                        tokCount,
                        absoluteTimings[w]!.Value.Start,
                        absoluteTimings[w]!.Value.End,
                        sliceStartSec,
                        t => t.Text.Length
                    );

                    var tokenSlice = new ArraySegment<Token>(tokens, tokStart, tokCount);
                    yield return new AudioSegment(
                        audioToEmit.AsMemory(),
                        OutputSampleRate,
                        tokenSlice
                    );

                    lastEmittedSample = wordEndSample;
                }
            }

            lastEmittedWordCount = confirmedWordCount;
        }

        // Final full CTC alignment for best accuracy
        if (totalSamplesEmitted > 0)
        {
            using var finalResult = _aligner.Align(
                CollectionsMarshal.AsSpan(accumulatedAudio),
                alignText,
                OutputSampleRate
            );
            for (var i = 0; i < finalResult.Count && i < ctcWords.Length; i++)
            {
                absoluteTimings[i] = (
                    finalResult.Timings[i].StartTime.TotalSeconds,
                    finalResult.Timings[i].EndTime.TotalSeconds
                );
            }

            for (var w = lastEmittedWordCount; w < finalResult.Count && w < ctcWords.Length; w++)
            {
                var wordEndSample = Math.Min(
                    (int)(absoluteTimings[w]!.Value.End * OutputSampleRate),
                    totalSamplesEmitted
                );

                if (wordEndSample > lastEmittedSample)
                {
                    var sliceStartSec = lastEmittedSample / (double)OutputSampleRate;
                    var emitLen = wordEndSample - lastEmittedSample;
                    var audioSlice = CollectionsMarshal
                        .AsSpan(accumulatedAudio)
                        .Slice(lastEmittedSample, emitLen)
                        .ToArray();

                    var (tokStart, tokCount) = wordToTokenRange[w];
                    TokenTimingUtils.DistributeTimings(
                        tokens,
                        tokStart,
                        tokCount,
                        absoluteTimings[w]!.Value.Start,
                        absoluteTimings[w]!.Value.End,
                        sliceStartSec,
                        t => t.Text.Length
                    );

                    var tokenSlice = new ArraySegment<Token>(tokens, tokStart, tokCount);
                    yield return new AudioSegment(
                        audioSlice.AsMemory(),
                        OutputSampleRate,
                        tokenSlice
                    );

                    lastEmittedSample = wordEndSample;
                }
            }

            // Trailing audio
            if (totalSamplesEmitted > lastEmittedSample)
            {
                var tailLength = totalSamplesEmitted - lastEmittedSample;
                var tailAudio = CollectionsMarshal
                    .AsSpan(accumulatedAudio)
                    .Slice(lastEmittedSample, tailLength)
                    .ToArray();

                yield return new AudioSegment(
                    tailAudio.AsMemory(),
                    OutputSampleRate,
                    Array.Empty<Token>()
                );
            }
        }
    }

    /// <summary>
    ///     Maps each CTC word (space-separated) to the range of phonemizer tokens it covers.
    ///     Walks through tokens, accumulating text+whitespace until each CTC word is matched.
    /// </summary>
    private static (int Start, int Count)[] BuildWordToTokenMapping(
        Token[] tokens,
        string[] ctcWords
    )
    {
        var mapping = new (int Start, int Count)[ctcWords.Length];
        var tokenIdx = 0;

        for (var wordIdx = 0; wordIdx < ctcWords.Length; wordIdx++)
        {
            var start = tokenIdx;
            var accumulated = new System.Text.StringBuilder();

            // Accumulate token text until we've covered this CTC word
            while (tokenIdx < tokens.Length)
            {
                accumulated.Append(tokens[tokenIdx].Text);
                var accStr = accumulated.ToString();
                tokenIdx++;

                // Check if we've accumulated enough text to cover this CTC word.
                // CTC word is formed by joining token texts without whitespace between
                // tokens that are part of the same space-separated word.
                if (accStr.Length >= ctcWords[wordIdx].Length)
                {
                    break;
                }
            }

            mapping[wordIdx] = (start, tokenIdx - start);
        }

        return mapping;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _llama.Dispose();
        _decoderSession.Dispose();
        _disposed = true;
    }

    private IEnumerable<int[]> GenerateCodeFrames(
        float[] initialLogits,
        float[] initialHidden,
        int prefillLen,
        Qwen3GenerationOptions options,
        List<float>? entropyAccumulator = null
    )
    {
        var numCodeGroups = _config.CodecNumCodebooks;
        var codecEos = _config.CodecEosId;
        var maxSteps = Math.Min(options.MaxNewTokens, DefaultMaxNewTokens);

        var tokenHistory = new List<int>(Qwen3Sampler.RepetitionPenaltyWindow);
        var currentLogits = initialLogits;
        var currentHidden = initialHidden;

        var feedbackBuffer = new float[TalkerDim];
        var projectedHidden = new float[1024];

        // Silent frame detection (matches Rust: SILENT_PENALTY_THRESHOLD=4, MAX=8, HARD=15)
        const int silentCountThreshold = 100;
        const int silentPenaltyTokens = 8;
        const int silentPenaltyStart = 4;
        const int silentPenaltyMaxFrames = 8;
        const int silentHardStop = 15;
        const float silentPenaltyBase = 2.0f;
        var consecutiveSilent = 0;

        // Official min_new_tokens=2: suppress EOS for the first 2 steps
        const int minNewTokens = 2;

        var loopSw = Stopwatch.StartNew();
        var stepSw = new Stopwatch();
        const int progressInterval = 50;

        _logger?.LogDebug(
            "GenerateCodeFrames: starting, maxSteps={MaxSteps}, codecEos={CodecEos}",
            maxSteps,
            codecEos
        );

        var totalSteps = 0;

        for (var step = 0; step < maxSteps; step++)
        {
            stepSw.Restart();

            // Capture entropy for word timing estimation
            entropyAccumulator?.Add(ComputeLogitEntropy(currentLogits));

            // Silence penalty (optional, not in official Python implementation)
            var silencePenalty = 0f;
            if (options.SilencePenaltyEnabled)
            {
                if (consecutiveSilent >= silentPenaltyMaxFrames)
                {
                    silencePenalty = 100f;
                }
                else if (consecutiveSilent >= silentPenaltyStart)
                {
                    silencePenalty =
                        silentPenaltyBase
                        * (1.0f + (consecutiveSilent - silentPenaltyStart) * 0.5f);
                }
            }

            // Build sliding window span: last RepetitionPenaltyWindow tokens, zero-alloc
            var windowStart = Math.Max(
                0,
                tokenHistory.Count - Qwen3Sampler.RepetitionPenaltyWindow
            );
            var recentTokens = CollectionsMarshal.AsSpan(tokenHistory).Slice(windowStart);

            var group0Token = Qwen3Sampler.SampleTalkerToken(
                currentLogits,
                _llama.TalkerVocabSize,
                codecEos,
                recentTokens,
                options,
                silencePenalty,
                options.SilencePenaltyEnabled ? silentPenaltyTokens : 0,
                step < minNewTokens ? codecEos : -1
            );

            if (group0Token == codecEos)
            {
                _logger?.LogDebug(
                    "GenerateCodeFrames: EOS at step {Step} after {Elapsed}ms total",
                    step,
                    loopSw.ElapsedMilliseconds
                );

                break;
            }

            // Track silent frames (for penalty and hard stop)
            if (group0Token < silentCountThreshold)
            {
                consecutiveSilent++;
                if (options.SilencePenaltyEnabled && consecutiveSilent >= silentHardStop)
                {
                    _logger?.LogDebug(
                        "GenerateCodeFrames: hard stop at step {Step}, {Count} consecutive silent frames, {Elapsed}ms total",
                        step,
                        consecutiveSilent,
                        loopSw.ElapsedMilliseconds
                    );

                    break;
                }
            }
            else
            {
                consecutiveSilent = 0;
            }

            tokenHistory.Add(group0Token);

            var codes = new int[numCodeGroups];
            codes[0] = group0Token;

            // Project hidden 2048→1024 and run code predictor
            _embeddings.ProjectTo1024(currentHidden, projectedHidden);
            _llama.PredictCodes(
                projectedHidden,
                codes,
                numCodeGroups,
                options.Temperature,
                options.TopK,
                options.CodePredictorGreedy,
                _embeddings.GetCodecEmbedding1024
            );

            yield return codes;

            // Build feedback: sum of all codec embeddings + tts_pad
            _embeddings.BuildFeedbackEmbedding(codes, feedbackBuffer);

            // Talker decode with feedback (incremental, KV cache preserved)
            (currentLogits, currentHidden) = _llama.TalkerDecode(feedbackBuffer, prefillLen + step);

            totalSteps = step + 1;
            var stepMs = stepSw.ElapsedMilliseconds;

            if (stepMs > 5000)
            {
                _logger?.LogWarning(
                    "GenerateCodeFrames: step {Step} took {StepMs}ms (TalkerDecode + PredictCodes)",
                    step,
                    stepMs
                );
            }

            if (totalSteps % progressInterval == 0)
            {
                var elapsedMs = loopSw.ElapsedMilliseconds;
                var framesPerSec = totalSteps / (elapsedMs / 1000.0);
                _logger?.LogDebug(
                    "GenerateCodeFrames: {Steps}/{MaxSteps} frames in {Elapsed}ms ({Rate:F1} frames/s, ~{AudioSec:F1}s audio)",
                    totalSteps,
                    maxSteps,
                    elapsedMs,
                    framesPerSec,
                    totalSteps * SamplesPerFrame / (float)OutputSampleRate
                );
            }
        }

        if (totalSteps >= maxSteps)
        {
            _logger?.LogWarning(
                "GenerateCodeFrames: hit max steps limit ({MaxSteps}) without EOS after {Elapsed}ms",
                maxSteps,
                loopSw.ElapsedMilliseconds
            );
        }

        _logger?.LogDebug(
            "GenerateCodeFrames: finished, {TotalSteps} frames in {Elapsed}ms",
            totalSteps,
            loopSw.ElapsedMilliseconds
        );
    }

    /// <summary>
    ///     Applies a linear fade-out to the tail of an audio buffer to prevent
    ///     clicks/pops when playback stops at a non-zero sample.
    /// </summary>
    private static void ApplyFadeOut(float[] audio, int? fadeSamples = null)
    {
        var fadeLen = Math.Min(fadeSamples ?? FadeOutSamples, audio.Length);
        var fadeStart = audio.Length - fadeLen;

        for (var i = 0; i < fadeLen; i++)
        {
            audio[fadeStart + i] *= (fadeLen - i) / (float)fadeLen;
        }
    }

    /// <summary>
    ///     Computes Shannon entropy of a logit distribution (via numerically stable log-softmax).
    ///     Used for word timing estimation — entropy peaks correlate with word transitions.
    /// </summary>
    private static float ComputeLogitEntropy(float[] logits)
    {
        var max = float.MinValue;
        for (var i = 0; i < logits.Length; i++)
        {
            if (logits[i] > max)
            {
                max = logits[i];
            }
        }

        var sumExp = 0.0;
        for (var i = 0; i < logits.Length; i++)
        {
            sumExp += Math.Exp(logits[i] - max);
        }

        var logSumExp = max + Math.Log(sumExp);
        var entropy = 0.0;
        for (var i = 0; i < logits.Length; i++)
        {
            var logP = logits[i] - logSumExp;
            var p = Math.Exp(logP);
            if (p > 1e-10)
            {
                entropy -= p * logP;
            }
        }

        return (float)entropy;
    }
}
