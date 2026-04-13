using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Adapters.Audio.Output;

/// <summary>
/// Computes and exposes a live audio amplitude signal from playback events.
/// On each chunk start, builds an <see cref="AmplitudeEnvelope"/>; as
/// playback progresses, samples the envelope to update <see cref="CurrentAmplitude"/>
/// at envelope-window resolution (default ~33 Hz). UI consumers see real
/// per-syllable amplitude variation, not a single per-sentence average.
/// </summary>
public sealed class AudioAmplitudeProvider : IAudioAmplitudeProvider, IDisposable
{
    private readonly IAudioProgressNotifier _notifier;

    private AmplitudeEnvelope _envelope = AmplitudeEnvelope.Empty;
    private DateTimeOffset _chunkStartUtc;
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
        _envelope = AmplitudeEnvelope.From(data, e.Chunk.SampleRate);
        _chunkStartUtc = DateTimeOffset.UtcNow;
        _amplitude = _envelope.SampleAt(0f);
        _isPlaying = true;
    }

    private void OnPlaybackProgress(object? sender, AudioPlaybackProgressEvent e)
    {
        if (!_isPlaying)
            return;

        var elapsed = (float)(DateTimeOffset.UtcNow - _chunkStartUtc).TotalSeconds;
        _amplitude = Math.Clamp(_envelope.SampleAt(elapsed), 0f, 1f);
    }

    private void OnChunkEnded(object? sender, AudioChunkPlaybackEndedEvent e)
    {
        _isPlaying = false;
        _envelope = AmplitudeEnvelope.Empty;
    }

    public void Dispose()
    {
        _notifier.ChunkPlaybackStarted -= OnChunkStarted;
        _notifier.ChunkPlaybackEnded -= OnChunkEnded;
        _notifier.PlaybackProgress -= OnPlaybackProgress;
    }
}
