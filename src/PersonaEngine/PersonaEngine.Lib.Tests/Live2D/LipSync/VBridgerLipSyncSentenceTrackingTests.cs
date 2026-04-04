using Microsoft.Extensions.Logging;
using NSubstitute;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;
using PersonaEngine.Lib.Live2D.Behaviour.LipSync;
using PersonaEngine.Lib.TTS.Synthesis;
using Xunit;

namespace PersonaEngine.Lib.Tests.Live2D.LipSync;

public class VBridgerLipSyncSentenceTrackingTests
{
    private readonly IAudioProgressNotifier _notifier = Substitute.For<IAudioProgressNotifier>();
    private readonly ILogger<VBridgerLipSyncService> _logger = Substitute.For<
        ILogger<VBridgerLipSyncService>
    >();

    private VBridgerLipSyncService CreateService()
    {
        return new VBridgerLipSyncService(_logger, _notifier);
    }

    private void RaiseChunkStarted(AudioSegment chunk)
    {
        _notifier.ChunkPlaybackStarted += Raise.Event<EventHandler<AudioChunkPlaybackStartedEvent>>(
            this,
            new AudioChunkPlaybackStartedEvent(
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                chunk
            )
        );
    }

    private void RaiseChunkEnded(AudioSegment chunk)
    {
        _notifier.ChunkPlaybackEnded += Raise.Event<EventHandler<AudioChunkPlaybackEndedEvent>>(
            this,
            new AudioChunkPlaybackEndedEvent(
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                chunk
            )
        );
    }

    private static AudioSegment MakeSegment(
        Guid sentenceId,
        float durationSeconds,
        int sampleRate = 24000,
        IReadOnlyList<Token>? tokens = null
    )
    {
        var sampleCount = (int)(durationSeconds * sampleRate);
        return new AudioSegment(
            new float[sampleCount].AsMemory(),
            sampleRate,
            tokens ?? Array.Empty<Token>()
        )
        {
            SentenceId = sentenceId,
        };
    }

    private static List<Token> MakeTokens(
        params (string Text, string Phonemes, double Start, double End)[] items
    )
    {
        return items
            .Select(t => new Token
            {
                Text = t.Text,
                Phonemes = t.Phonemes,
                StartTs = t.Start,
                EndTs = t.End,
                Whitespace = " ",
            })
            .ToList();
    }

    [Fact]
    public void SameSentenceChunk_WithoutTokens_PreservesActivePhonemes()
    {
        // Arrange: first chunk has tokens, second chunk (same sentence) has none
        var sentenceId = Guid.NewGuid();
        var tokens = MakeTokens(("hello", "hɛloʊ", 0.0, 0.5));
        var chunk1 = MakeSegment(sentenceId, 0.3f, tokens: tokens);
        var chunk2 = MakeSegment(sentenceId, 0.3f); // no tokens

        var service = CreateService();

        // Act: chunk1 populates phonemes
        RaiseChunkStarted(chunk1);
        var phonemesAfterChunk1 = service._activePhonemes.Count;

        // chunk1 ends, chunk2 starts (same sentence, no tokens)
        RaiseChunkEnded(chunk1);
        RaiseChunkStarted(chunk2);

        // Assert: phonemes should be preserved across same-sentence empty chunks
        Assert.True(phonemesAfterChunk1 > 0, "Chunk1 should have populated phonemes");
        Assert.Equal(phonemesAfterChunk1, service._activePhonemes.Count);
    }

    [Fact]
    public void SameSentenceChunk_WithoutTokens_KeepsPlaying()
    {
        // Arrange
        var sentenceId = Guid.NewGuid();
        var tokens = MakeTokens(("hello", "hɛloʊ", 0.0, 0.5));
        var chunk1 = MakeSegment(sentenceId, 0.3f, tokens: tokens);
        var chunk2 = MakeSegment(sentenceId, 0.3f);

        var service = CreateService();

        // Act
        RaiseChunkStarted(chunk1);
        RaiseChunkEnded(chunk1);

        // Assert: _isPlaying should remain true between same-sentence chunks
        Assert.True(service._isPlaying, "Should stay playing between same-sentence chunks");

        RaiseChunkStarted(chunk2);
        Assert.True(service._isPlaying, "Should be playing during chunk2");
    }

    [Fact]
    public void NewSentence_ClearsAndReplacesPhonemes()
    {
        // Arrange: two chunks from different sentences
        var sentence1 = Guid.NewGuid();
        var sentence2 = Guid.NewGuid();
        var tokens1 = MakeTokens(("hello", "hɛloʊ", 0.0, 0.5));
        var tokens2 = MakeTokens(("world", "wɜld", 0.0, 0.4));
        var chunk1 = MakeSegment(sentence1, 0.5f, tokens: tokens1);
        var chunk2 = MakeSegment(sentence2, 0.4f, tokens: tokens2);

        var service = CreateService();

        // Act
        RaiseChunkStarted(chunk1);
        var phonemesAfterChunk1 = service._activePhonemes.Count;

        RaiseChunkEnded(chunk1);
        RaiseChunkStarted(chunk2);
        var phonemesAfterChunk2 = service._activePhonemes.Count;

        // Assert: new sentence should have replaced phonemes (not accumulated)
        Assert.True(phonemesAfterChunk1 > 0);
        Assert.True(phonemesAfterChunk2 > 0);
        // "world"/"wɜld" has fewer phoneme chars than "hello"/"hɛloʊ"
        Assert.NotEqual(phonemesAfterChunk1, phonemesAfterChunk2);
    }

    [Fact]
    public void SameSentenceChunk_WithUpdatedTokens_ReplacesPhonemes()
    {
        // Arrange: first chunk has estimate, later chunk has refined timing
        var sentenceId = Guid.NewGuid();
        var initialTokens = MakeTokens(("hello", "hɛloʊ", 0.0, 1.0));
        var refinedTokens = MakeTokens(("hello", "hɛloʊ", 0.0, 0.6));
        var chunk1 = MakeSegment(sentenceId, 0.3f, tokens: initialTokens);
        var chunk2 = MakeSegment(sentenceId, 0.3f);
        var chunk3 = MakeSegment(sentenceId, 0.3f, tokens: refinedTokens);

        var service = CreateService();

        // Act
        RaiseChunkStarted(chunk1);
        var phonemesAfterChunk1 = service._activePhonemes.ToList();

        RaiseChunkEnded(chunk1);
        RaiseChunkStarted(chunk2); // no tokens, preserves

        RaiseChunkEnded(chunk2);
        RaiseChunkStarted(chunk3); // has tokens, replaces

        // Assert: phonemes should be refreshed with refined timing
        Assert.Equal(phonemesAfterChunk1.Count, service._activePhonemes.Count);
        // End times should differ due to refined timing
        Assert.NotEqual(
            phonemesAfterChunk1[^1].EndTime,
            service._activePhonemes[^1].EndTime
        );
    }
}
