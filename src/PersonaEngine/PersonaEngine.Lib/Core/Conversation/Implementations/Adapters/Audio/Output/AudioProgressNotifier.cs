using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Adapters.Audio.Output;

public class AudioProgressNotifier : IAudioProgressNotifier
{
    private readonly ILogger<AudioProgressNotifier> _logger;

    public AudioProgressNotifier(ILogger<AudioProgressNotifier> logger)
    {
        _logger = logger;
    }

    public event EventHandler<AudioChunkPlaybackStartedEvent>? ChunkPlaybackStarted;

    public event EventHandler<AudioChunkPlaybackEndedEvent>? ChunkPlaybackEnded;

    public event EventHandler<AudioPlaybackProgressEvent>? PlaybackProgress;

    public void RaiseChunkStarted(object? sender, AudioChunkPlaybackStartedEvent args)
    {
        _logger.LogTrace(
            "Raising ChunkPlaybackStarted event for TurnId: {TurnId}, Chunk Id: {Sequence}",
            args.TurnId,
            args.Chunk.Id
        );
        SafeInvokeIsolated(ChunkPlaybackStarted, sender, args);
    }

    public void RaiseChunkEnded(object? sender, AudioChunkPlaybackEndedEvent args)
    {
        _logger.LogTrace(
            "Raising ChunkPlaybackEnded event for TurnId: {TurnId}, Chunk Id: {Sequence}",
            args.TurnId,
            args.Chunk.Id
        );
        SafeInvokeIsolated(ChunkPlaybackEnded, sender, args);
    }

    public void RaiseProgress(object? sender, AudioPlaybackProgressEvent args)
    {
        // Direct multi-cast invoke — the compiler emits a zero-allocation iteration over
        // the multicast delegate chain. One outer try/catch; a throwing handler short-circuits
        // the remainder of its tick but not subsequent ticks. Acceptable for a pure
        // "tell me where you are" event fired at ~240 Hz during playback.
        var handler = PlaybackProgress;
        if (handler is null)
            return;

        try
        {
            handler(sender, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in audio progress event handler (PlaybackProgress).");
        }
    }

    /// <summary>
    ///     Iterates the invocation list to isolate failures handler-by-handler. Incurs a
    ///     <c>Delegate[]</c> allocation per call — acceptable for low-frequency state-change
    ///     events, not for the per-tick progress stream.
    /// </summary>
    private void SafeInvokeIsolated<TEventArgs>(
        EventHandler<TEventArgs>? eventHandler,
        object? sender,
        TEventArgs args
    )
    {
        if (eventHandler == null)
        {
            return;
        }

        var invocationList = eventHandler.GetInvocationList();

        foreach (var handlerDelegate in invocationList)
        {
            try
            {
                var handler = (EventHandler<TEventArgs>)handlerDelegate;
                handler(sender, args);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Exception in audio progress event handler ({EventType}).",
                    typeof(TEventArgs).Name
                );
            }
        }
    }
}
