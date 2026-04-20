using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaEngine.Lib.IO;
using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.TTS.Synthesis.Alignment;
using PersonaEngine.Lib.TTS.Synthesis.Qwen3;
using Xunit;
using Xunit.Abstractions;

namespace PersonaEngine.Lib.Tests.TTS.Qwen3;

/// <summary>
///     Diagnostic test to analyze CTC forced alignment confidence scores
///     when aligning partial audio against full-sentence text.
///     This reveals how the Viterbi path score degrades for unspoken words.
/// </summary>
public class CtcConfidenceAnalysisTests
{
    /// <summary>
    ///     Resolves the model base directory using the same convention as the
    ///     runtime: <c>AppContext.BaseDirectory/Resources</c>. Override via the
    ///     <c>TTS_MODEL_BASE_DIR</c> environment variable when running against a
    ///     non-default install root (e.g. CI staging).
    /// </summary>
    private static string GetModelBaseDir()
    {
        return Environment.GetEnvironmentVariable("TTS_MODEL_BASE_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "Resources");
    }

    private readonly ITestOutputHelper _output;

    public CtcConfidenceAnalysisTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static IModelProvider? TryCreateModelProvider(string baseDir)
    {
        // The runtime FileModelProvider constructor throws when the base
        // directory is missing — that's correct for the app, but in tests we
        // want to skip cleanly when the optional Qwen3/CTC models aren't
        // present (the tree under Resources/ is gitignored and only populated
        // by the bootstrapper or a manual download).
        if (!Directory.Exists(baseDir))
            return null;

        return new FileModelProvider(baseDir, NullLogger<FileModelProvider>.Instance);
    }

    private static IForcedAligner CreateAligner(IModelProvider modelProvider)
    {
        return new CtcForcedAligner(modelProvider, NullLogger<CtcForcedAligner>.Instance);
    }

    private static bool ModelsExist(IModelProvider provider)
    {
        return File.Exists(provider.GetModelPath(IO.ModelType.Qwen3.Talker))
            && File.Exists(
                Path.Combine(
                    provider.GetModelPath(IO.ModelType.Qwen3.Embeddings),
                    "text_embedding_projected.npy"
                )
            );
    }

    private static bool CtcModelsExist(IModelProvider provider)
    {
        return File.Exists(provider.GetModelPath(IO.ModelType.Ctc.Model))
            && File.Exists(provider.GetModelPath(IO.ModelType.Ctc.Vocab));
    }

    /// <summary>
    ///     Generates the full audio for a sentence, then runs CTC alignment at
    ///     progressively larger audio prefixes (simulating streaming) to observe
    ///     how per-word confidence degrades for words not yet spoken.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task AnalyzeCtcConfidence_AtProgressiveAudioPrefixes()
    {
        var baseDir = GetModelBaseDir();
        var provider = TryCreateModelProvider(baseDir);
        if (provider is null)
        {
            _output.WriteLine($"SKIP: model base directory not found: {baseDir}");
            return;
        }

        if (!ModelsExist(provider))
        {
            _output.WriteLine("SKIP: GGUF models not found");
            return;
        }

        if (!CtcModelsExist(provider))
        {
            _output.WriteLine("SKIP: CTC (wav2vec2) models not found");
            return;
        }

        var text = "Hello world, this is a streaming test for lip sync.";
        var words = text.Split(' ');

        _output.WriteLine($"Text: \"{text}\"");
        _output.WriteLine($"Words: {words.Length}");
        _output.WriteLine("");

        // Step 1: Generate the complete audio
        using var aligner = CreateAligner(provider);
        using var engine = Qwen3TtsGgufEngine.Load(provider, aligner);
        using var decoder = engine.CreateAudioDecoder();

        var allAudioChunks = new List<float[]>();
        await foreach (
            var chunk in engine.GenerateStreaming(
                decoder,
                text,
                isLastSegment: true,
                speaker: "ryan",
                language: "english",
                options: new Qwen3GenerationOptions { MaxNewTokens = 2048 }
            )
        )
        {
            allAudioChunks.Add(chunk);
        }

        var totalSamples = allAudioChunks.Sum(c => c.Length);
        var fullAudio = new float[totalSamples];
        var offset = 0;
        foreach (var chunk in allAudioChunks)
        {
            Array.Copy(chunk, 0, fullAudio, offset, chunk.Length);
            offset += chunk.Length;
        }

        var totalDuration = (float)totalSamples / Qwen3TtsGgufEngine.OutputSampleRate;
        _output.WriteLine($"Full audio: {totalDuration:F2}s ({totalSamples} samples)");
        _output.WriteLine("");

        // Step 2: Run CTC alignment on the complete audio (ground truth)
        using var fullResult = aligner.Align(fullAudio, text, Qwen3TtsGgufEngine.OutputSampleRate);
        _output.WriteLine("=== GROUND TRUTH (full audio) ===");
        for (var i = 0; i < fullResult.Count; i++)
        {
            var wt = fullResult.Timings[i];
            _output.WriteLine(
                $"  [{wt.StartTime.TotalSeconds:F3}s - {wt.EndTime.TotalSeconds:F3}s] "
                    + $"confidence={wt.Confidence:F3} \"{wt.Word}\""
            );
        }

        _output.WriteLine("");

        // Step 3: Run CTC at progressive audio prefixes (25%, 50%, 75%, 100%)
        var prefixFractions = new[] { 0.25f, 0.50f, 0.75f, 1.0f };

        foreach (var fraction in prefixFractions)
        {
            var prefixSamples = (int)(totalSamples * fraction);
            var prefixAudio = fullAudio.AsSpan(0, prefixSamples);
            var prefixDuration = (float)prefixSamples / Qwen3TtsGgufEngine.OutputSampleRate;

            using var prefixResult = aligner.Align(
                prefixAudio,
                text,
                Qwen3TtsGgufEngine.OutputSampleRate
            );

            _output.WriteLine(
                $"=== PREFIX {fraction:P0} ({prefixDuration:F2}s / {totalDuration:F2}s) ==="
            );

            for (var i = 0; i < prefixResult.Count; i++)
            {
                var wt = prefixResult.Timings[i];
                var durationMs = wt.Duration.TotalMilliseconds;

                // Compare timing to ground truth
                var gtMatch = i < fullResult.Count ? fullResult.Timings[i] : default;
                var timeShift =
                    i < fullResult.Count
                        ? Math.Abs(wt.StartTime.TotalSeconds - gtMatch.StartTime.TotalSeconds)
                        : -1;

                // Flag words that are probably not spoken in this prefix
                var isPastAudio = wt.StartTime.TotalSeconds > prefixDuration * 0.95;
                var flag = isPastAudio ? " <<< PAST AUDIO" : "";
                var compressed = durationMs < 20 ? " <<< COMPRESSED" : "";

                _output.WriteLine(
                    $"  [{wt.StartTime.TotalSeconds:F3}s - {wt.EndTime.TotalSeconds:F3}s] "
                        + $"dur={durationMs:F0}ms conf={wt.Confidence:F3} "
                        + $"gt_shift={timeShift:F3}s "
                        + $"\"{wt.Word}\"{flag}{compressed}"
                );
            }

            _output.WriteLine("");
        }
    }
}
