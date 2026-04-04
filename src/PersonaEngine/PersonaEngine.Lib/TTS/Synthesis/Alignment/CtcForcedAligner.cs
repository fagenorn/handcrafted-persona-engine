using System.Buffers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using PersonaEngine.Lib.Audio;
using PersonaEngine.Lib.IO;

namespace PersonaEngine.Lib.TTS.Synthesis.Alignment;

/// <summary>
///     CTC forced alignment using wav2vec2-base-960h ONNX model.
///     Provides precise word-level timing (20ms resolution) by aligning
///     known text against audio using character-level CTC probabilities.
///     Supports incremental alignment on partial audio for streaming use.
/// </summary>
public sealed class CtcForcedAligner : IForcedAligner
{
    private const uint Wav2VecSampleRate = 16000;
    private const int BlankIdx = 0; // <pad> is the CTC blank
    private const int SeparatorIdx = 4; // "|" is word boundary
    private const int VocabSize = 32;

    private readonly InferenceSession _session;
    private readonly Dictionary<char, int> _charToIdx;
    private readonly ILogger? _logger;
    private bool _disposed;

    public CtcForcedAligner(IModelProvider modelProvider, ILogger<CtcForcedAligner>? logger = null)
    {
        var modelPath = modelProvider.GetModelPath(IO.ModelType.Ctc.Model);
        var vocabPath = modelProvider.GetModelPath(IO.ModelType.Ctc.Vocab);

        var opts = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };
        _session = new InferenceSession(modelPath, opts);

        // Load vocabulary: char → index mapping
        var vocabJson = JsonSerializer.Deserialize<Dictionary<string, int>>(
            File.ReadAllText(vocabPath)
        )!;
        _charToIdx = new Dictionary<char, int>();
        foreach (var (key, value) in vocabJson)
        {
            if (key.Length == 1)
            {
                _charToIdx[key[0]] = value;
            }
        }

        _logger = logger;
    }

    /// <inheritdoc />
    public AlignmentResult Align(ReadOnlySpan<float> audio, string text, int sampleRate)
    {
        var audioDurationSec = audio.Length / (double)sampleRate;

        // Resample to 16kHz if needed
        float[]? resampledRent = null;
        ReadOnlySpan<float> audio16Khz;

        if ((uint)sampleRate == Wav2VecSampleRate)
        {
            audio16Khz = audio;
        }
        else
        {
            var targetFrames = AudioConverter.CalculateResampledFrameCount(
                audio.Length,
                (uint)sampleRate,
                Wav2VecSampleRate
            );
            resampledRent = ArrayPool<float>.Shared.Rent(targetFrames);
            AudioConverter.ResampleFloat(
                audio.ToArray().AsMemory(),
                resampledRent.AsMemory(0, targetFrames),
                channels: 1,
                (uint)sampleRate,
                Wav2VecSampleRate
            );
            audio16Khz = resampledRent.AsSpan(0, targetFrames);
        }

        try
        {
            // Run wav2vec2 forward pass → log probabilities [1, T, 32]
            var logProbs = RunWav2Vec(audio16Khz);
            var numFrames = logProbs.Length / VocabSize;
            var frameDuration = audioDurationSec / numFrames;

            // Prepare text for CTC: uppercase, replace space with |
            var ctcText = text.ToUpperInvariant().Replace(' ', '|');

            // Build CTC label sequence with interleaved blanks
            var labels = BuildCtcLabels(ctcText);

            // Run Viterbi forced alignment
            var path = ViterbiAlign(logProbs, numFrames, labels);

            // Extract character boundaries and group into words
            return ExtractWordTimings(path, labels, ctcText, frameDuration, logProbs);
        }
        finally
        {
            if (resampledRent is not null)
            {
                ArrayPool<float>.Shared.Return(resampledRent);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _session.Dispose();
        _disposed = true;
    }

    private float[] RunWav2Vec(ReadOnlySpan<float> audio16Khz)
    {
        var audioArray = audio16Khz.ToArray();
        var inputShape = new long[] { 1, audioArray.Length };
        using var inputOrt = OrtValue.CreateTensorValueFromMemory(
            OrtMemoryInfo.DefaultInstance,
            audioArray.AsMemory(),
            inputShape
        );

        var inputNames = new[] { "input_values" };
        var outputNames = new[] { "logits" };

        using var results = _session.Run(new RunOptions(), inputNames, [inputOrt], outputNames);

        // Output shape: [1, T, 32] — apply log_softmax
        var logitsSpan = results[0].GetTensorDataAsSpan<float>();
        var logProbs = new float[logitsSpan.Length];

        var numFrames = logitsSpan.Length / VocabSize;
        for (var t = 0; t < numFrames; t++)
        {
            var offset = t * VocabSize;

            // Log-softmax for this frame
            var max = float.NegativeInfinity;
            for (var v = 0; v < VocabSize; v++)
            {
                if (logitsSpan[offset + v] > max)
                {
                    max = logitsSpan[offset + v];
                }
            }

            var sumExp = 0f;
            for (var v = 0; v < VocabSize; v++)
            {
                sumExp += MathF.Exp(logitsSpan[offset + v] - max);
            }

            var logSumExp = max + MathF.Log(sumExp);
            for (var v = 0; v < VocabSize; v++)
            {
                logProbs[offset + v] = logitsSpan[offset + v] - logSumExp;
            }
        }

        return logProbs;
    }

    /// <summary>
    ///     Builds CTC label sequence: blank, char, blank, char, blank, ...
    /// </summary>
    private int[] BuildCtcLabels(string ctcText)
    {
        var labels = new int[2 * ctcText.Length + 1];
        labels[0] = BlankIdx;
        for (var i = 0; i < ctcText.Length; i++)
        {
            var c = ctcText[i];
            labels[2 * i + 1] = c == '|' ? SeparatorIdx : _charToIdx.GetValueOrDefault(c, BlankIdx);
            labels[2 * i + 2] = BlankIdx;
        }

        return labels;
    }

    /// <summary>
    ///     Viterbi forced alignment through CTC trellis.
    ///     Returns the optimal path as (frameIdx, labelIdx) pairs.
    /// </summary>
    private static List<(int Frame, int Label)> ViterbiAlign(
        float[] logProbs,
        int numFrames,
        int[] labels
    )
    {
        var S = labels.Length;

        // Rent pooled arrays for trellis and backpointers
        var trellisRent = ArrayPool<float>.Shared.Rent(numFrames * S);
        var backptrRent = ArrayPool<int>.Shared.Rent(numFrames * S);
        var trellis = trellisRent.AsSpan(0, numFrames * S);
        var backptr = backptrRent.AsSpan(0, numFrames * S);

        try
        {
            // Initialize with -inf
            trellis.Fill(float.NegativeInfinity);
            backptr.Clear();

            // t=0: can start at blank (j=0) or first char (j=1)
            trellis[0 * S + 0] = logProbs[0 * VocabSize + labels[0]];
            if (S > 1)
            {
                trellis[0 * S + 1] = logProbs[0 * VocabSize + labels[1]];
            }

            // Forward pass
            for (var t = 1; t < numFrames; t++)
            {
                for (var j = 0; j < S; j++)
                {
                    var labelIdx = labels[j];
                    var emission = logProbs[t * VocabSize + labelIdx];
                    var bestScore = float.NegativeInfinity;
                    var bestBack = 0;

                    // Option 1: stay at j
                    var stayScore = trellis[(t - 1) * S + j];
                    if (stayScore > bestScore)
                    {
                        bestScore = stayScore;
                        bestBack = 0;
                    }

                    // Option 2: from j-1 (advance one label position)
                    if (j > 0)
                    {
                        var prevLabel = labels[j - 1];
                        if (labelIdx == BlankIdx || labelIdx != prevLabel)
                        {
                            var advScore = trellis[(t - 1) * S + (j - 1)];
                            if (advScore > bestScore)
                            {
                                bestScore = advScore;
                                bestBack = -1;
                            }
                        }
                    }

                    // Option 3: from j-2 (skip blank for repeated chars)
                    if (j > 1 && labelIdx != BlankIdx)
                    {
                        var twoBackLabel = labels[j - 2];
                        if (labelIdx != twoBackLabel)
                        {
                            var skipScore = trellis[(t - 1) * S + (j - 2)];
                            if (skipScore > bestScore)
                            {
                                bestScore = skipScore;
                                bestBack = -2;
                            }
                        }
                    }

                    if (bestScore > float.NegativeInfinity)
                    {
                        trellis[t * S + j] = bestScore + emission;
                        backptr[t * S + j] = bestBack;
                    }
                }
            }

            // Backtrack from the best end position
            var endJ = S - 1;
            if (
                S > 1
                && trellis[(numFrames - 1) * S + S - 2] > trellis[(numFrames - 1) * S + S - 1]
            )
            {
                endJ = S - 2;
            }

            var path = new List<(int Frame, int Label)>(numFrames);
            var currentJ = endJ;
            for (var t = numFrames - 1; t >= 0; t--)
            {
                path.Add((t, currentJ));
                var back = backptr[t * S + currentJ];
                currentJ += back;
            }

            path.Reverse();
            return path;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(trellisRent);
            ArrayPool<int>.Shared.Return(backptrRent);
        }
    }

    /// <summary>
    ///     Extracts word timings from the Viterbi path into a pooled buffer.
    ///     Computes per-word confidence as the mean emission log-probability
    ///     along the path frames assigned to each word's characters.
    /// </summary>
    private AlignmentResult ExtractWordTimings(
        List<(int Frame, int Label)> path,
        int[] labels,
        string ctcText,
        double frameDuration,
        float[] logProbs
    )
    {
        // Map path to character segments (skip blanks), collecting emission scores
        var charSegments =
            new List<(
                int CharIdx,
                int StartFrame,
                int EndFrame,
                float SumLogProb,
                int FrameCount
            )>();
        var currentCharIdx = -1;
        var currentStartFrame = 0;
        var currentSumLogProb = 0f;
        var currentFrameCount = 0;

        foreach (var (frame, labelPos) in path)
        {
            if (labelPos % 2 == 0)
            {
                continue; // Skip blank positions (even indices)
            }

            var charIdx = labelPos / 2;
            var labelIdx = labels[labelPos];
            var emission = logProbs[frame * VocabSize + labelIdx];

            if (charIdx != currentCharIdx)
            {
                if (currentCharIdx >= 0)
                {
                    charSegments.Add(
                        (
                            currentCharIdx,
                            currentStartFrame,
                            frame - 1,
                            currentSumLogProb,
                            currentFrameCount
                        )
                    );
                }

                currentCharIdx = charIdx;
                currentStartFrame = frame;
                currentSumLogProb = emission;
                currentFrameCount = 1;
            }
            else
            {
                currentSumLogProb += emission;
                currentFrameCount++;
            }
        }

        if (currentCharIdx >= 0)
        {
            charSegments.Add(
                (
                    currentCharIdx,
                    currentStartFrame,
                    path[^1].Frame,
                    currentSumLogProb,
                    currentFrameCount
                )
            );
        }

        // Group character segments into words (split on '|') — use pooled buffer
        // Worst case: every other char is a word → (ctcText.Length + 1) / 2
        var maxWords = (ctcText.Length + 1) / 2 + 1;
        var buffer = ArrayPool<WordTiming>.Shared.Rent(maxWords);
        var wordCount = 0;

        var currentWord = "";
        var wordStartFrame = -1;
        var wordEndFrame = -1;
        var wordSumLogProb = 0f;
        var wordFrameCount = 0;

        foreach (var (charIdx, startFrame, endFrame, sumLogProb, frameCount) in charSegments)
        {
            var c = ctcText[charIdx];

            if (c == '|')
            {
                if (currentWord.Length > 0)
                {
                    var confidence = wordFrameCount > 0 ? wordSumLogProb / wordFrameCount : 0f;
                    buffer[wordCount++] = new WordTiming(
                        currentWord,
                        TimeSpan.FromSeconds(wordStartFrame * frameDuration),
                        TimeSpan.FromSeconds((wordEndFrame + 1) * frameDuration),
                        confidence
                    );
                    currentWord = "";
                    wordStartFrame = -1;
                    wordSumLogProb = 0f;
                    wordFrameCount = 0;
                }
            }
            else
            {
                if (wordStartFrame < 0)
                {
                    wordStartFrame = startFrame;
                }

                currentWord += c;
                wordEndFrame = endFrame;
                wordSumLogProb += sumLogProb;
                wordFrameCount += frameCount;
            }
        }

        // Flush last word
        if (currentWord.Length > 0)
        {
            var confidence = wordFrameCount > 0 ? wordSumLogProb / wordFrameCount : 0f;
            buffer[wordCount++] = new WordTiming(
                currentWord,
                TimeSpan.FromSeconds(wordStartFrame * frameDuration),
                TimeSpan.FromSeconds((wordEndFrame + 1) * frameDuration),
                confidence
            );
        }

        _logger?.LogDebug(
            "CTC aligned {WordCount} words across {Frames} frames ({Duration:F2}s)",
            wordCount,
            path.Count,
            path.Count * frameDuration
        );

        return new AlignmentResult(buffer, wordCount);
    }
}
