using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;
using PersonaEngine.Lib.IO;
using PersonaEngine.Lib.Live2D.Behaviour.LipSync;
using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.TTS.Synthesis.Alignment;
using PersonaEngine.Lib.TTS.Synthesis.Qwen3;
using Xunit;
using Xunit.Abstractions;

namespace PersonaEngine.Lib.Tests.TTS.Qwen3;

/// <summary>
///     Integration tests verifying the full streaming TTS → CTC alignment → phoneme
///     enrichment → lip-sync pipeline works correctly for Qwen3.
/// </summary>
public class Qwen3StreamingLipSyncIntegrationTests
{
    private const string DefaultModelBaseDir =
        @"C:\Users\anisa\Projects\handcrafted-persona-engine\src\PersonaEngine\PersonaEngine.Lib\Resources\Models";

    private readonly ITestOutputHelper _output;

    public Qwen3StreamingLipSyncIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string GetModelBaseDir()
    {
        return Environment.GetEnvironmentVariable("TTS_MODEL_BASE_DIR") ?? DefaultModelBaseDir;
    }

    private static IModelProvider CreateModelProvider(string baseDir)
    {
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
    ///     Simulates TtsOrchestrator.EnrichTokensWithPhonemes — merges phoneme data
    ///     from a phonemizer lookup into engine-produced tokens by word text match.
    /// </summary>
    private static void EnrichTokensWithPhonemes(
        IReadOnlyList<Token> engineTokens,
        Dictionary<string, string> phonemeLookup
    )
    {
        foreach (var token in engineTokens)
        {
            if (!string.IsNullOrEmpty(token.Phonemes) || string.IsNullOrEmpty(token.Text))
            {
                continue;
            }

            if (phonemeLookup.TryGetValue(token.Text.ToLowerInvariant(), out var phonemes))
            {
                token.Phonemes = phonemes;
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StreamingPipeline_ProducesValidTimingsAndPhonemes_ForLipSync()
    {
        // Arrange
        var baseDir = GetModelBaseDir();
        var provider = CreateModelProvider(baseDir);
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

        // Simulated phonemizer output (word → IPA phonemes)
        var phonemeLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hello"] = "hɛloʊ",
            ["world"] = "wɜːld",
            ["this"] = "ðɪs",
            ["is"] = "ɪz",
            ["a"] = "ə",
            ["streaming"] = "stɹiːmɪŋ",
            ["test"] = "tɛst",
            ["for"] = "fɔːɹ",
            ["lip"] = "lɪp",
            ["sync"] = "sɪŋk",
        };

        using var aligner = CreateAligner(provider);
        using var engine = Qwen3TtsGgufEngine.Load(provider, aligner);
        using var decoder = engine.CreateAudioDecoder();

        var sentenceId = Guid.NewGuid();
        var allSegments = new List<AudioSegment>();
        var chunksWithTokens = 0;
        var chunksWithoutTokens = 0;
        var totalAudioSamples = 0;

        _output.WriteLine($"Text: \"{text}\"");
        _output.WriteLine($"SentenceId: {sentenceId}");
        _output.WriteLine("");

        // Act: Generate streaming audio with timings (simulating what the session does)
        var sw = Stopwatch.StartNew();
        await foreach (
            var segment in engine.GenerateStreamingWithTimings(
                decoder,
                text,
                isLastSegment: true,
                speaker: "ryan",
                language: "english",
                options: new Qwen3GenerationOptions { MaxNewTokens = 2048 }
            )
        )
        {
            // Simulate TtsOrchestrator: stamp SentenceId
            var stamped = segment with
            {
                SentenceId = sentenceId,
            };

            // Simulate TtsOrchestrator: enrich tokens with phonemes
            if (stamped.Tokens.Count > 0)
            {
                EnrichTokensWithPhonemes(stamped.Tokens, phonemeLookup);
                chunksWithTokens++;

                _output.WriteLine(
                    $"Chunk #{allSegments.Count + 1}: {stamped.AudioData.Length} samples, "
                        + $"{stamped.Tokens.Count} tokens (enriched with phonemes)"
                );

                foreach (var token in stamped.Tokens)
                {
                    _output.WriteLine(
                        $"  [{token.StartTs:F3}s - {token.EndTs:F3}s] "
                            + $"\"{token.Text}\" → {token.Phonemes ?? "(no phonemes)"}"
                    );
                }
            }
            else
            {
                chunksWithoutTokens++;
                _output.WriteLine(
                    $"Chunk #{allSegments.Count + 1}: {stamped.AudioData.Length} samples, no tokens"
                );
            }

            totalAudioSamples += stamped.AudioData.Length;
            allSegments.Add(stamped);
        }

        sw.Stop();

        var totalDuration = (float)totalAudioSamples / Qwen3TtsGgufEngine.OutputSampleRate;
        _output.WriteLine("");
        _output.WriteLine($"=== SUMMARY ===");
        _output.WriteLine($"Total chunks: {allSegments.Count}");
        _output.WriteLine($"Chunks with tokens: {chunksWithTokens}");
        _output.WriteLine($"Chunks without tokens: {chunksWithoutTokens}");
        _output.WriteLine($"Total audio: {totalDuration:F2}s ({totalAudioSamples} samples)");
        _output.WriteLine($"Wall time: {sw.ElapsedMilliseconds}ms");

        // Assert: streaming structure
        Assert.True(allSegments.Count > 1, "Should produce multiple streaming chunks");
        Assert.True(chunksWithTokens >= 2, "Should have at least initial estimate + final CTC");
        Assert.True(
            chunksWithoutTokens > 0,
            "Should have empty-token chunks between timing updates"
        );

        // Assert: all segments share the same SentenceId
        Assert.All(allSegments, s => Assert.Equal(sentenceId, s.SentenceId));

        // Assert: final timing tokens are valid
        var lastTokenSegment = allSegments.Last(s => s.Tokens.Count > 0);
        var finalTokens = lastTokenSegment.Tokens;

        Assert.True(finalTokens.Count > 0, "Final timing update should have tokens");

        // Check monotonic ordering
        for (var i = 1; i < finalTokens.Count; i++)
        {
            Assert.True(
                finalTokens[i].StartTs >= finalTokens[i - 1].StartTs,
                $"Token timings should be monotonically ordered: "
                    + $"token[{i - 1}].Start={finalTokens[i - 1].StartTs:F3} > token[{i}].Start={finalTokens[i].StartTs:F3}"
            );
        }

        // Check timing coverage (CTC should cover most of the audio)
        var lastEnd = finalTokens[^1].EndTs ?? 0;
        var coverage = lastEnd / totalDuration;
        _output.WriteLine(
            $"CTC timing coverage: {coverage:P1} ({lastEnd:F2}s / {totalDuration:F2}s)"
        );
        Assert.True(coverage > 0.5, $"CTC timings should cover >50% of audio, got {coverage:P1}");

        // Assert: phoneme enrichment worked on final tokens
        var enrichedCount = finalTokens.Count(t => !string.IsNullOrEmpty(t.Phonemes));
        var enrichmentRate = (float)enrichedCount / finalTokens.Count;
        _output.WriteLine(
            $"Phoneme enrichment: {enrichedCount}/{finalTokens.Count} tokens ({enrichmentRate:P0})"
        );
        Assert.True(
            enrichedCount > 0,
            "At least some tokens should have phonemes after enrichment"
        );

        // Assert: enriched tokens have valid phoneme strings
        foreach (var token in finalTokens.Where(t => !string.IsNullOrEmpty(t.Phonemes)))
        {
            Assert.True(
                token.Phonemes!.Length > 0,
                $"Token \"{token.Text}\" phonemes should not be empty"
            );
            Assert.True(
                token.StartTs.HasValue && token.EndTs.HasValue,
                $"Token \"{token.Text}\" should have timing data"
            );
            Assert.True(
                token.EndTs > token.StartTs,
                $"Token \"{token.Text}\" EndTs ({token.EndTs:F3}) should be > StartTs ({token.StartTs:F3})"
            );
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StreamingPipeline_LipSyncReceivesCorrectPhonemes_AcrossChunks()
    {
        // Arrange
        var baseDir = GetModelBaseDir();
        var provider = CreateModelProvider(baseDir);
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

        var text = "Testing the lip sync pipeline.";

        var phonemeLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["testing"] = "tɛstɪŋ",
            ["the"] = "ðə",
            ["lip"] = "lɪp",
            ["sync"] = "sɪŋk",
            ["pipeline"] = "paɪplaɪn",
        };

        using var aligner = CreateAligner(provider);
        using var engine = Qwen3TtsGgufEngine.Load(provider, aligner);
        using var decoder = engine.CreateAudioDecoder();

        var sentenceId = Guid.NewGuid();

        // Collect all segments with SentenceId stamped and phonemes enriched
        var allSegments = new List<AudioSegment>();
        await foreach (
            var segment in engine.GenerateStreamingWithTimings(
                decoder,
                text,
                isLastSegment: true,
                speaker: "ryan",
                language: "english",
                options: new Qwen3GenerationOptions { MaxNewTokens = 2048 }
            )
        )
        {
            var stamped = segment with { SentenceId = sentenceId };
            if (stamped.Tokens.Count > 0)
            {
                EnrichTokensWithPhonemes(stamped.Tokens, phonemeLookup);
            }
            allSegments.Add(stamped);
        }

        Assert.True(allSegments.Count > 1, "Should produce multiple chunks");

        // Now simulate playback through VBridgerLipSyncService
        var notifier = Substitute.For<IAudioProgressNotifier>();
        var logger = Substitute.For<ILogger<VBridgerLipSyncService>>();
        var lipSync = new VBridgerLipSyncService(logger, notifier);
        lipSync._isStarted = true;

        var cumulativeOffset = 0.0;

        _output.WriteLine("=== SIMULATING PLAYBACK ===");

        foreach (var segment in allSegments)
        {
            // Raise ChunkStarted
            notifier.ChunkPlaybackStarted += Raise.Event<
                EventHandler<AudioChunkPlaybackStartedEvent>
            >(
                this,
                new AudioChunkPlaybackStartedEvent(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    segment
                )
            );

            var hasTokens = segment.Tokens.Count > 0;
            _output.WriteLine(
                $"Chunk: {segment.AudioData.Length} samples, "
                    + $"tokens={segment.Tokens.Count}, "
                    + $"offset={cumulativeOffset:F3}s, "
                    + $"activePhonemes={lipSync._activePhonemes.Count}"
            );

            // Verify phonemes are preserved across empty-token chunks
            if (!hasTokens && lipSync._activePhonemes.Count > 0)
            {
                _output.WriteLine(
                    $"  ✓ Phonemes preserved ({lipSync._activePhonemes.Count} active)"
                );
            }

            // Simulate progress events at a few points within the chunk
            if (segment.AudioData.Length > 0)
            {
                var chunkDuration = segment.DurationInSeconds;
                var progressPoints = new[] { 0.0, chunkDuration * 0.5, chunkDuration * 0.9 };

                foreach (var t in progressPoints)
                {
                    notifier.PlaybackProgress += Raise.Event<
                        EventHandler<AudioPlaybackProgressEvent>
                    >(
                        this,
                        new AudioPlaybackProgressEvent(
                            Guid.NewGuid(),
                            Guid.NewGuid(),
                            DateTimeOffset.UtcNow,
                            TimeSpan.FromSeconds(t)
                        )
                    );
                }
            }

            // Raise ChunkEnded
            notifier.ChunkPlaybackEnded += Raise.Event<EventHandler<AudioChunkPlaybackEndedEvent>>(
                this,
                new AudioChunkPlaybackEndedEvent(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    segment
                )
            );

            cumulativeOffset += segment.DurationInSeconds;
        }

        _output.WriteLine("");
        _output.WriteLine($"Final state:");
        _output.WriteLine($"  _isPlaying: {lipSync._isPlaying}");
        _output.WriteLine($"  _activePhonemes: {lipSync._activePhonemes.Count}");
        _output.WriteLine($"  _cumulativeTimeOffset: {lipSync._cumulativeTimeOffset:F3}s");
        _output.WriteLine($"  _currentSentenceId: {lipSync._currentSentenceId}");

        // Assert: lip-sync state is correct after full playback
        Assert.Equal(sentenceId, lipSync._currentSentenceId);
        Assert.True(lipSync._activePhonemes.Count > 0, "Should have active phonemes at end");
        Assert.True(
            lipSync._cumulativeTimeOffset > 0,
            "Should have accumulated time offset across chunks"
        );

        // Assert: phonemes in the lip-sync service have valid timing
        var phonemes = lipSync._activePhonemes;
        _output.WriteLine($"\n=== ACTIVE PHONEMES ({phonemes.Count}) ===");
        for (var i = 0; i < Math.Min(phonemes.Count, 20); i++)
        {
            _output.WriteLine(
                $"  [{phonemes[i].StartTime:F3}s - {phonemes[i].EndTime:F3}s] \"{phonemes[i].Phoneme}\""
            );
        }

        if (phonemes.Count > 20)
        {
            _output.WriteLine($"  ... ({phonemes.Count - 20} more)");
        }

        // Phonemes should be sorted by start time
        for (var i = 1; i < phonemes.Count; i++)
        {
            Assert.True(
                phonemes[i].StartTime >= phonemes[i - 1].StartTime,
                $"Phonemes should be sorted: [{i - 1}].Start={phonemes[i - 1].StartTime:F3} > [{i}].Start={phonemes[i].StartTime:F3}"
            );
        }

        // Phonemes should have non-zero duration
        Assert.All(
            phonemes,
            p =>
                Assert.True(
                    p.EndTime > p.StartTime,
                    $"Phoneme \"{p.Phoneme}\" should have positive duration"
                )
        );
    }
}
