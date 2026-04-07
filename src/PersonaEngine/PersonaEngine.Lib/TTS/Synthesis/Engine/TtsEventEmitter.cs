using System.Threading.Channels;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;

namespace PersonaEngine.Lib.TTS.Synthesis.Engine;

/// <summary>
///     Encapsulates the TTS lifecycle event protocol for a single conversation turn.
///     Tracks primed/first-chunk state internally and writes events to the output channel.
///     Methods that must succeed unconditionally (stream end, error) use
///     <see cref="CancellationToken.None" /> to avoid swallowing events on cancellation.
/// </summary>
internal sealed class TtsEventEmitter
{
    private readonly ChannelWriter<IOutputEvent> _writer;
    private readonly Guid _sessionId;
    private readonly Guid _turnId;

    private bool _primed = true;
    private bool _firstChunk = true;

    public TtsEventEmitter(ChannelWriter<IOutputEvent> writer, Guid sessionId, Guid turnId)
    {
        _writer = writer;
        _sessionId = sessionId;
        _turnId = turnId;
    }

    public CompletionReason CompletionReason { get; private set; } = CompletionReason.Completed;

    /// <summary>
    ///     Emits <see cref="TtsReadyToSynthesizeEvent" /> on the first call. Subsequent calls are no-ops.
    /// </summary>
    public async ValueTask EmitReadyToSynthesizeAsync()
    {
        if (!_primed)
        {
            return;
        }

        _primed = false;
        await _writer
            .WriteAsync(
                new TtsReadyToSynthesizeEvent(_sessionId, _turnId, DateTimeOffset.UtcNow),
                CancellationToken.None
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Emits <see cref="TtsStreamStartEvent" /> on the first call, then <see cref="TtsChunkEvent" /> on every call.
    /// </summary>
    public async ValueTask EmitChunkAsync(AudioSegment segment)
    {
        if (_firstChunk)
        {
            _firstChunk = false;
            await _writer
                .WriteAsync(
                    new TtsStreamStartEvent(_sessionId, _turnId, DateTimeOffset.UtcNow),
                    CancellationToken.None
                )
                .ConfigureAwait(false);
        }

        await _writer
            .WriteAsync(
                new TtsChunkEvent(_sessionId, _turnId, DateTimeOffset.UtcNow, segment),
                CancellationToken.None
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Emits <see cref="TtsStreamEndEvent" />. Called in finally blocks — uses
    ///     <see cref="CancellationToken.None" /> to guarantee delivery.
    ///     Emits if any chunks were produced, or if an error occurred (to prevent FSM hang).
    ///     No-op if no chunks and no error.
    /// </summary>
    public async ValueTask EmitStreamEndAsync()
    {
        if (_firstChunk && CompletionReason != CompletionReason.Error)
        {
            return;
        }

        await _writer
            .WriteAsync(
                new TtsStreamEndEvent(_sessionId, _turnId, DateTimeOffset.UtcNow, CompletionReason),
                CancellationToken.None
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Emits <see cref="ErrorOutputEvent" /> and sets <see cref="CompletionReason" /> to Error.
    ///     Uses <see cref="CancellationToken.None" /> to guarantee delivery.
    /// </summary>
    public async ValueTask EmitErrorAsync(Exception ex)
    {
        CompletionReason = CompletionReason.Error;
        await _writer
            .WriteAsync(
                new ErrorOutputEvent(_sessionId, _turnId, DateTimeOffset.UtcNow, ex),
                CancellationToken.None
            )
            .ConfigureAwait(false);
    }

    public void SetReason(CompletionReason reason)
    {
        CompletionReason = reason;
    }
}
