using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;

public interface IAudioProgressNotifier
{
    event EventHandler<AudioChunkPlaybackStartedEvent>? ChunkPlaybackStarted;

    event EventHandler<AudioChunkPlaybackEndedEvent>? ChunkPlaybackEnded;

    event EventHandler<AudioPlaybackProgressEvent>? PlaybackProgress;

    void RaiseChunkStarted(object? sender, AudioChunkPlaybackStartedEvent args);

    void RaiseChunkEnded(object? sender, AudioChunkPlaybackEndedEvent args);

    void RaiseProgress(object? sender, AudioPlaybackProgressEvent args);
}