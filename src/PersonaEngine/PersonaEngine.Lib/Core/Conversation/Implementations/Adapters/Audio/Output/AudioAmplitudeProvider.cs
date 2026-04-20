using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Adapters.Audio.Output;

/// <summary>
///     Computes and exposes a live audio amplitude signal from playback events.
///     On each chunk start, (re)builds an <see cref="AmplitudeEnvelope" /> owned for the
///     provider's lifetime; as playback progresses, samples the envelope at the elapsed
///     time reported by the <see cref="AudioPlaybackProgressEvent" />.
/// </summary>
public sealed class AudioAmplitudeProvider : IAudioAmplitudeProvider, IDisposable
{
    private readonly IAudioProgressNotifier _notifier;

    private readonly AmplitudeEnvelope _envelope = new();
    private float _amplitude;
    private bool _isPlaying;

    public AudioAmplitudeProvider(IAudioProgressNotifier notifier)
    {
        _notifier = notifier;
        _notifier.ChunkPlaybackStarted += OnChunkStarted;
        _notifier.ChunkPlaybackEnded += OnChunkEnded;
        _notifier.PlaybackProgress += OnPlaybackProgress;
    }

    public float CurrentAmplitude => _amplitude;

    public bool IsPlaying => _isPlaying;

    private void OnChunkStarted(object? sender, AudioChunkPlaybackStartedEvent e)
    {
        var data = e.Chunk.AudioData.Span;
        _envelope.RebuildFrom(data, e.Chunk.SampleRate);
        _amplitude = _envelope.SampleAt(0f);
        _isPlaying = true;
    }

    private void OnPlaybackProgress(object? sender, AudioPlaybackProgressEvent e)
    {
        if (!_isPlaying)
            return;

        var elapsed = (float)e.CurrentPlaybackTime.TotalSeconds;
        _amplitude = Math.Clamp(_envelope.SampleAt(elapsed), 0f, 1f);
    }

    private void OnChunkEnded(object? sender, AudioChunkPlaybackEndedEvent e)
    {
        _isPlaying = false;
        _envelope.Reset();
    }

    public void Dispose()
    {
        _notifier.ChunkPlaybackStarted -= OnChunkStarted;
        _notifier.ChunkPlaybackEnded -= OnChunkEnded;
        _notifier.PlaybackProgress -= OnPlaybackProgress;
    }
}
