using System.Threading.Channels;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;

namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Top-level TTS pipeline: reads LLM streaming chunks and produces
///     TTS lifecycle events (start, chunks, end) on the output channel.
/// </summary>
public interface ITtsEngine : IDisposable
{
    /// <summary>
    ///     True when the engine is currently able to start a synthesis session.
    ///     Becomes false after a failed attempt to create an underlying session
    ///     and flips back to true on the next successful attempt.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    ///     Message from the most recent session-creation failure, or <c>null</c>
    ///     when <see cref="IsReady" /> is true.
    /// </summary>
    string? LastInitError { get; }

    /// <summary>
    ///     Raised on transitions of <see cref="IsReady" /> / <see cref="LastInitError" />.
    ///     Subscribers must read <see cref="IsReady" /> / <see cref="LastInitError" />
    ///     to observe the current state — the event carries no payload.
    /// </summary>
    event Action? ReadyChanged;

    Task<CompletionReason> SynthesizeStreamingAsync(
        ChannelReader<LlmChunkEvent> inputReader,
        ChannelWriter<IOutputEvent> outputWriter,
        Guid turnId,
        Guid sessionId,
        CancellationToken cancellationToken = default
    );
}
