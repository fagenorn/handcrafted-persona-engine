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
    private readonly Dictionary<int, char> _idxToChar;
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

        _idxToChar = new Dictionary<int, char>();
        foreach (var (key, value) in _charToIdx)
        {
            _idxToChar[value] = key;
        }

        _idxToChar[SeparatorIdx] = '|';

        _logger = logger;
    }

    /// <inheritdoc />
    public AlignmentResult Align(ReadOnlySpan<float> audio, string text, int sampleRate)
    {
        var audioDurationSec = audio.Length / (double)sampleRate;
        var audio16Khz = ResampleTo16Khz(audio, sampleRate, out var resampledRent);

        try
        {
            var logProbs = RunWav2Vec(audio16Khz);
            var numFrames = logProbs.Length / VocabSize;
            var frameDuration = audioDurationSec / numFrames;

            var ctcText = text.ToUpperInvariant().Replace(' ', '|');
            var labels = BuildCtcLabels(ctcText);
            var path = ViterbiAlign(logProbs, numFrames, labels);

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

    /// <inheritdoc />
    public AlignmentResult AlignSpokenWindowed(
        ReadOnlySpan<float> audioWindow,
        string remainingText,
        int sampleRate,
        double windowStartTime
    )
    {
        var windowDurationSec = audioWindow.Length / (double)sampleRate;
        var audio16Khz = ResampleTo16Khz(audioWindow, sampleRate, out var resampledRent);

        try
        {
            var logProbs = RunWav2Vec(audio16Khz);
            var numFrames = logProbs.Length / VocabSize;
            var frameDuration = windowDurationSec / numFrames;

            // Greedy decode on the window to see which remaining words are present
            var greedyText = GreedyDecode(logProbs, numFrames);
            var spokenCount = CountSpokenWords(greedyText, remainingText);

            _logger?.LogDebug(
                "AlignSpokenWindowed: window={WindowStart:F2}s-{WindowEnd:F2}s, greedy=\"{Greedy}\", spoken={Spoken}",
                windowStartTime,
                windowStartTime + windowDurationSec,
                greedyText.Replace('|', ' ').Trim(),
                spokenCount
            );

            if (spokenCount == 0)
            {
                var emptyBuffer = ArrayPool<WordTiming>.Shared.Rent(1);
                return new AlignmentResult(emptyBuffer, 0);
            }

            // Viterbi-align only the confirmed words within this window
            var words = remainingText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var prefixText = string.Join(' ', words.Take(spokenCount));
            var ctcText = prefixText.ToUpperInvariant().Replace(' ', '|');
            var labels = BuildCtcLabels(ctcText);
            var path = ViterbiAlign(logProbs, numFrames, labels);

            // Extract timings relative to window, then offset to absolute time
            var windowResult = ExtractWordTimings(path, labels, ctcText, frameDuration, logProbs);

            // Offset all timings by windowStartTime to get absolute times
            var buffer = ArrayPool<WordTiming>.Shared.Rent(windowResult.Count);
            for (var i = 0; i < windowResult.Count; i++)
            {
                var wt = windowResult.Timings[i];
                buffer[i] = new WordTiming(
                    wt.Word,
                    wt.StartTime + TimeSpan.FromSeconds(windowStartTime),
                    wt.EndTime + TimeSpan.FromSeconds(windowStartTime),
                    wt.Confidence
                );
            }

            var count = windowResult.Count;
            windowResult.Dispose(); // return the inner buffer

            return new AlignmentResult(buffer, count);
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

    /// <summary>
    ///     Resamples audio to 16kHz for wav2vec2. Returns the 16kHz span via the return value
    ///     and optionally a rented buffer via <paramref name="rentedBuffer" /> that the caller
    ///     must return to the pool when done.
    /// </summary>
    private static ReadOnlySpan<float> ResampleTo16Khz(
        ReadOnlySpan<float> audio,
        int sampleRate,
        out float[]? rentedBuffer
    )
    {
        if ((uint)sampleRate == Wav2VecSampleRate)
        {
            rentedBuffer = null;

            return audio;
        }

        var targetFrames = AudioConverter.CalculateResampledFrameCount(
            audio.Length,
            (uint)sampleRate,
            Wav2VecSampleRate
        );
        rentedBuffer = ArrayPool<float>.Shared.Rent(targetFrames);
        AudioConverter.ResampleFloat(
            audio.ToArray().AsMemory(),
            rentedBuffer.AsMemory(0, targetFrames),
            channels: 1,
            (uint)sampleRate,
            Wav2VecSampleRate
        );

        return rentedBuffer.AsSpan(0, targetFrames);
    }

    /// <summary>
    ///     Greedy CTC decode: argmax each frame, collapse consecutive duplicates, remove blanks.
    ///     Returns recognized text with '|' as word separators.
    /// </summary>
    private string GreedyDecode(float[] logProbs, int numFrames)
    {
        var sb = new System.Text.StringBuilder();
        var prevIdx = -1;

        for (var t = 0; t < numFrames; t++)
        {
            var offset = t * VocabSize;
            var bestIdx = 0;
            var bestScore = logProbs[offset];
            for (var v = 1; v < VocabSize; v++)
            {
                if (logProbs[offset + v] > bestScore)
                {
                    bestScore = logProbs[offset + v];
                    bestIdx = v;
                }
            }

            if (bestIdx == prevIdx)
            {
                prevIdx = bestIdx;
                continue;
            }

            prevIdx = bestIdx;

            if (bestIdx == BlankIdx)
            {
                continue;
            }

            if (_idxToChar.TryGetValue(bestIdx, out var c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Counts how many consecutive words from the transcript were recognized
    ///     in the greedy CTC output. Uses Levenshtein similarity for fuzzy matching.
    ///     Skips leading greedy words that don't match (fragments from window overlap).
    /// </summary>
    private static int CountSpokenWords(string greedyText, string transcript)
    {
        var greedyWords = greedyText
            .ToUpperInvariant()
            .Split('|', StringSplitOptions.RemoveEmptyEntries);

        var transcriptWords = transcript
            .ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (greedyWords.Length == 0 || transcriptWords.Length == 0)
        {
            return 0;
        }

        // Try starting from greedy positions 0-2 (skip overlap fragments)
        var bestMatchedWords = 0;

        var maxSkip = Math.Min(3, greedyWords.Length);
        for (var startIdx = 0; startIdx < maxSkip; startIdx++)
        {
            var matchedWords = 0;
            var greedyIdx = startIdx;

            foreach (var tWordRaw in transcriptWords)
            {
                if (greedyIdx >= greedyWords.Length)
                {
                    break;
                }

                var tWord = StripPunctuation(tWordRaw);
                if (tWord.Length == 0)
                {
                    continue;
                }

                var gWord = greedyWords[greedyIdx];
                var similarity = WordSimilarity(gWord, tWord);

                if (similarity >= 0.5f)
                {
                    matchedWords++;
                    greedyIdx++;
                }
                else
                {
                    break;
                }
            }

            if (matchedWords > bestMatchedWords)
            {
                bestMatchedWords = matchedWords;
            }
        }

        return bestMatchedWords;
    }

    private static string StripPunctuation(string word)
    {
        var sb = new System.Text.StringBuilder(word.Length);
        foreach (var c in word)
        {
            if (char.IsLetter(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static float WordSimilarity(string a, string b)
    {
        if (a == b)
        {
            return 1.0f;
        }

        var maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0)
        {
            return 1.0f;
        }

        var dist = LevenshteinDistance(a, b);

        return 1.0f - (float)dist / maxLen;
    }

    private static int LevenshteinDistance(string s, string t)
    {
        var n = s.Length;
        var m = t.Length;
        var d = new int[n + 1, m + 1];

        for (var i = 0; i <= n; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= m; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost
                );
            }
        }

        return d[n, m];
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
