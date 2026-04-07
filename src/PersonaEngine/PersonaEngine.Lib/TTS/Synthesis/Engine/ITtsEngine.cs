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
    Task<CompletionReason> SynthesizeStreamingAsync(
        ChannelReader<LlmChunkEvent> inputReader,
        ChannelWriter<IOutputEvent> outputWriter,
        Guid turnId,
        Guid sessionId,
        CancellationToken cancellationToken = default
    );
}
