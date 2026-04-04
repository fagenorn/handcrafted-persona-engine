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
    public void SameSentenceChunk_WithoutTokens_DoesNotThrow()
    {
        // Arrange: first chunk has tokens, second chunk (same sentence) has none
        var sentenceId = Guid.NewGuid();
        var tokens = MakeTokens(("hello", "hɛloʊ", 0.0, 0.5));
        var chunk1 = MakeSegment(sentenceId, 0.3f, tokens: tokens);
        var chunk2 = MakeSegment(sentenceId, 0.3f);

        var service = CreateService();

        // Act: simulate chunk1 start → end → chunk2 start → progress during chunk2
        _notifier.ChunkPlaybackStarted += Raise.Event<EventHandler<AudioChunkPlaybackStartedEvent>>(
            this,
            new AudioChunkPlaybackStartedEvent(
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                chunk1
            )
        );
        _notifier.ChunkPlaybackEnded += Raise.Event<EventHandler<AudioChunkPlaybackEndedEvent>>(
            this,
            new AudioChunkPlaybackEndedEvent(
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                chunk1
            )
        );
        _notifier.ChunkPlaybackStarted += Raise.Event<EventHandler<AudioChunkPlaybackStartedEvent>>(
            this,
            new AudioChunkPlaybackStartedEvent(
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                chunk2
            )
        );
        _notifier.PlaybackProgress += Raise.Event<EventHandler<AudioPlaybackProgressEvent>>(
            this,
            new AudioPlaybackProgressEvent(
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(0.1)
            )
        );

        // Assert: service handled multi-chunk sentence without errors
        // Full visual verification requires Live2D native deps (integration test)
    }

    [Fact]
    public void NewSentence_DoesNotThrow()
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
        _notifier.ChunkPlaybackStarted += Raise.Event<EventHandler<AudioChunkPlaybackStartedEvent>>(
            this,
            new AudioChunkPlaybackStartedEvent(
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                chunk1
            )
        );
        _notifier.ChunkPlaybackEnded += Raise.Event<EventHandler<AudioChunkPlaybackEndedEvent>>(
            this,
            new AudioChunkPlaybackEndedEvent(
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                chunk1
            )
        );
        _notifier.ChunkPlaybackStarted += Raise.Event<EventHandler<AudioChunkPlaybackStartedEvent>>(
            this,
            new AudioChunkPlaybackStartedEvent(
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                chunk2
            )
        );

        // Assert: no errors on sentence transition
    }
}
