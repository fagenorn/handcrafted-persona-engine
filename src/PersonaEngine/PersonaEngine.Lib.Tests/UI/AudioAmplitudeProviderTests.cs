using NSubstitute;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Implementations.Adapters.Audio.Output;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;
using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.TTS.Synthesis.LipSync;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI;

public class AudioAmplitudeProviderTests
{
    private static AudioSegment CreateSegment(float[] samples, int sampleRate = 24000)
    {
        return new AudioSegment(samples.AsMemory(), sampleRate, Array.Empty<Token>());
    }

    private static void RaiseChunkStarted(IAudioProgressNotifier notifier, AudioSegment segment)
    {
        notifier.ChunkPlaybackStarted += Raise.Event<EventHandler<AudioChunkPlaybackStartedEvent>>(
            notifier,
            new AudioChunkPlaybackStartedEvent(
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                segment
            )
        );
    }

    private static void RaiseChunkEnded(IAudioProgressNotifier notifier, AudioSegment segment)
    {
        notifier.ChunkPlaybackEnded += Raise.Event<EventHandler<AudioChunkPlaybackEndedEvent>>(
            notifier,
            new AudioChunkPlaybackEndedEvent(
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                segment
            )
        );
    }

    [Fact]
    public void CurrentAmplitude_Initially_IsZero()
    {
        var notifier = Substitute.For<IAudioProgressNotifier>();
        var provider = new AudioAmplitudeProvider(notifier);

        Assert.Equal(0f, provider.CurrentAmplitude);
    }

    [Fact]
    public void IsPlaying_Initially_IsFalse()
    {
        var notifier = Substitute.For<IAudioProgressNotifier>();
        var provider = new AudioAmplitudeProvider(notifier);

        Assert.False(provider.IsPlaying);
    }

    [Fact]
    public void OnChunkStarted_SetsAmplitude_FromAudioData()
    {
        var notifier = Substitute.For<IAudioProgressNotifier>();
        var provider = new AudioAmplitudeProvider(notifier);

        // Create a chunk with known amplitude: all 0.5 → RMS = 0.5
        var samples = new float[2400];
        Array.Fill(samples, 0.5f);
        var segment = CreateSegment(samples);

        RaiseChunkStarted(notifier, segment);

        Assert.True(provider.CurrentAmplitude > 0f);
        Assert.True(provider.IsPlaying);
    }

    [Fact]
    public void OnChunkEnded_SetsIsPlaying_ToFalse()
    {
        var notifier = Substitute.For<IAudioProgressNotifier>();
        var provider = new AudioAmplitudeProvider(notifier);

        var samples = new float[2400];
        Array.Fill(samples, 0.5f);
        var segment = CreateSegment(samples);

        RaiseChunkStarted(notifier, segment);
        RaiseChunkEnded(notifier, segment);

        Assert.False(provider.IsPlaying);
    }

}
