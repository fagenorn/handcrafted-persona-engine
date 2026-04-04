using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using PersonaEngine.Lib.IO;
using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.TTS.Synthesis.Alignment;

namespace PersonaEngine.Lib.TTS.Synthesis.Qwen3;

/// <summary>
///     Qwen3-TTS inference engine using GGUF models via LLamaSharp (llama.cpp).
///     Talker + Code Predictor run through llama.cpp for near-zero per-call overhead.
///     Audio decoder uses ONNX Runtime (streaming stateful decoder).
///     Output: 24 kHz mono float32 PCM.
/// </summary>
internal sealed class Qwen3TtsGgufEngine : IDisposable
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
        var decoderOpts = new SessionOptions
        {
            EnableMemoryPattern = true,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };
        decoderOpts.AppendExecutionProvider_CUDA();
        var decoderSession = new InferenceSession(
            modelProvider.GetModelPath(IO.ModelType.Qwen3.Decoder),
            decoderOpts
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
    public StreamingAudioDecoder CreateAudioDecoder() => new(_decoderSession, _logger);

    /// <summary>
    ///     Generates speech audio in streaming chunks.
    /// </summary>
    /// <param name="decoder">
    ///     Caller-owned streaming decoder. State is preserved across calls for
    ///     cross-sentence audio continuity.
    /// </param>
    /// <param name="isLastSegment">
    ///     True if this is the final segment in the turn. When true, the decoder's
    ///     conv buffers are flushed and a fade-out is applied.
    /// </param>
    public async IAsyncEnumerable<float[]> GenerateStreaming(
        StreamingAudioDecoder decoder,
        string text,
        bool isLastSegment,
        string speaker = "ryan",
        string language = "english",
        string? instruct = null,
        Qwen3GenerationOptions? options = null,
        int emitEveryFrames = 4,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        options ??= new Qwen3GenerationOptions();
        var sw = Stopwatch.StartNew();

        var (prefix, textBody, _) = _tokenizer.BuildCustomVoicePrompt(text);
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
                    foreach (var codes in GenerateCodeFrames(logits, hidden, prefillLen, options))
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
    ///     Generates speech audio in streaming chunks with word-level timing.
    ///     Strategy:
    ///       1. First chunk: proportional estimate (immediate, zero latency)
    ///       2. After ~1s accumulated audio: CTC forced alignment replaces estimate
    ///       3. Subsequent CTC refinements every ~1s of new audio
    ///     Tokens are emitted on the first chunk and on each CTC refinement.
    /// </summary>
    public async IAsyncEnumerable<AudioSegment> GenerateStreamingWithTimings(
        StreamingAudioDecoder decoder,
        string text,
        bool isLastSegment,
        string speaker = "ryan",
        string language = "english",
        string? instruct = null,
        Qwen3GenerationOptions? options = null,
        int emitEveryFrames = 4,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var words = SplitIntoWords(text);
        var accumulatedAudio = new List<float[]>();
        var totalSamplesEmitted = 0;
        var isFirstChunk = true;
        var lastCtcAlignAt = 0; // samples count at last CTC run
        const int ctcIntervalSamples = OutputSampleRate; // re-align every ~1s

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
                cancellationToken
            )
        )
        {
            accumulatedAudio.Add(audioChunk);
            totalSamplesEmitted += audioChunk.Length;

            IReadOnlyList<Token> tokens;

            if (isFirstChunk)
            {
                // Immediate proportional estimate for first chunk
                var estimatedDurationSec = 1.0 + text.Length * 0.06;
                tokens = ComputeWordTimings(words, estimatedDurationSec);
                isFirstChunk = false;
            }
            else if (totalSamplesEmitted - lastCtcAlignAt >= ctcIntervalSamples)
            {
                // Run CTC alignment on accumulated audio
                var allAudio = ConcatAudio(accumulatedAudio, totalSamplesEmitted);
                using var ctcResult = _aligner.Align(allAudio, text, OutputSampleRate);
                tokens = ConvertCtcToTokens(ctcResult.Timings, words);
                lastCtcAlignAt = totalSamplesEmitted;
            }
            else
            {
                tokens = Array.Empty<Token>();
            }

            yield return new AudioSegment(audioChunk.AsMemory(), OutputSampleRate, tokens);
        }

        // Final CTC alignment on complete audio for best accuracy
        if (totalSamplesEmitted > lastCtcAlignAt)
        {
            var finalAudio = ConcatAudio(accumulatedAudio, totalSamplesEmitted);
            using var finalResult = _aligner.Align(finalAudio, text, OutputSampleRate);
            var finalTokens = ConvertCtcToTokens(finalResult.Timings, words);

            // Emit a zero-length audio segment carrying final refined timings
            yield return new AudioSegment(Memory<float>.Empty, OutputSampleRate, finalTokens);
        }
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

    private static List<(string Word, string Whitespace)> SplitIntoWords(string text)
    {
        var result = new List<(string, string)>();
        var matches = Regex.Matches(text, @"(\S+)(\s*)");

        foreach (Match m in matches)
        {
            result.Add((m.Groups[1].Value, m.Groups[2].Value));
        }

        return result;
    }

    private static IReadOnlyList<Token> ComputeWordTimings(
        List<(string Word, string Whitespace)> words,
        double totalDurationSec
    )
    {
        var totalChars = words.Sum(w => (double)w.Word.Length);
        if (totalChars == 0)
        {
            return Array.Empty<Token>();
        }

        var tokens = new Token[words.Count];
        var pos = 0.0;

        for (var i = 0; i < words.Count; i++)
        {
            var (word, ws) = words[i];
            var fraction = word.Length / totalChars;
            var duration = totalDurationSec * fraction;

            tokens[i] = new Token
            {
                Text = word,
                Whitespace = ws,
                StartTs = pos,
                EndTs = pos + duration,
            };

            pos += duration;
        }

        return tokens;
    }

    private static float[] ConcatAudio(List<float[]> chunks, int totalSamples)
    {
        var result = new float[totalSamples];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            Array.Copy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }

        return result;
    }

    /// <summary>
    ///     Converts CTC word timings to Token objects, truncating at the
    ///     spoken/unspoken boundary detected via confidence cliff.
    /// </summary>
    private static IReadOnlyList<Token> ConvertCtcToTokens(
        ReadOnlySpan<WordTiming> ctcTimings,
        List<(string Word, string Whitespace)> words
    )
    {
        var spokenCount = FindSpokenWordCount(ctcTimings);
        var tokenCount = Math.Min(spokenCount, words.Count);

        if (tokenCount == 0)
        {
            return Array.Empty<Token>();
        }

        var tokens = new Token[tokenCount];

        for (var i = 0; i < tokenCount; i++)
        {
            var (word, ws) = words[i];
            tokens[i] = new Token
            {
                Text = word,
                Whitespace = ws,
                StartTs = ctcTimings[i].StartTime.TotalSeconds,
                EndTs = ctcTimings[i].EndTime.TotalSeconds,
            };
        }

        return tokens;
    }

    /// <summary>
    ///     Determines how many words were actually spoken by finding the largest
    ///     confidence drop between consecutive words. A drop exceeding the minimum
    ///     cliff gap indicates the transition from spoken to unspoken.
    /// </summary>
    private static int FindSpokenWordCount(ReadOnlySpan<WordTiming> timings)
    {
        const float minCliffGap = 5.0f;

        if (timings.Length <= 1)
        {
            return timings.Length;
        }

        var maxDrop = 0f;
        var cliffIndex = timings.Length;

        for (var i = 0; i < timings.Length - 1; i++)
        {
            var drop = timings[i].Confidence - timings[i + 1].Confidence;
            if (drop > maxDrop)
            {
                maxDrop = drop;
                cliffIndex = i + 1;
            }
        }

        return maxDrop > minCliffGap ? cliffIndex : timings.Length;
    }

    private IEnumerable<int[]> GenerateCodeFrames(
        float[] initialLogits,
        float[] initialHidden,
        int prefillLen,
        Qwen3GenerationOptions options
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
}
