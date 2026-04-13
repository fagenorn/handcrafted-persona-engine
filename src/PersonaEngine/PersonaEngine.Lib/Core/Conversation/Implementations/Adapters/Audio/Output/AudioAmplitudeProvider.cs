using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Adapters.Audio.Output;

/// <summary>
/// Computes and exposes smoothed audio amplitude from playback chunk events.
/// </summary>
public sealed class AudioAmplitudeProvider : IAudioAmplitudeProvider, IDisposable
{
    private readonly IAudioProgressNotifier _notifier;
    private float _amplitude;
    private bool _isPlaying;

    public AudioAmplitudeProvider(IAudioProgressNotifier notifier)
    {
        _notifier = notifier;
        _notifier.ChunkPlaybackStarted += OnChunkStarted;
        _notifier.ChunkPlaybackEnded += OnChunkEnded;
    }

    public float CurrentAmplitude => _amplitude;

    public bool IsPlaying => _isPlaying;

    private void OnChunkStarted(object? sender, AudioChunkPlaybackStartedEvent e)
    {
        var audioData = e.Chunk.AudioData;
        if (audioData.Length > 0)
        {
            _amplitude = Math.Clamp(ComputeRms(audioData.Span), 0f, 1f);
        }

        _isPlaying = true;
    }

    private void OnChunkEnded(object? sender, AudioChunkPlaybackEndedEvent e)
    {
        _isPlaying = false;
    }

    public static float ComputeRms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
            return 0f;

        var sum = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * samples[i];
        }

        return MathF.Sqrt(sum / samples.Length);
    }

    public void Dispose()
    {
        _notifier.ChunkPlaybackStarted -= OnChunkStarted;
        _notifier.ChunkPlaybackEnded -= OnChunkEnded;
    }
}
