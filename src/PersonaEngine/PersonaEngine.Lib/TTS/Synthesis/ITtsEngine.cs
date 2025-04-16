using System.Threading.Channels;

using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;

namespace PersonaEngine.Lib.TTS.Synthesis;

public interface ITtsEngine : IDisposable
{
    Task<CompletionReason> SynthesizeStreamingAsync(
        ChannelReader<LlmChunkEvent> inputReader,
        ChannelWriter<IOutputEvent>  outputWriter,
        Guid                         turnId,
        Guid                         sessionId,
        KokoroVoiceOptions?          options           = null,
        CancellationToken            cancellationToken = default
    );
        
    IAsyncEnumerable<AudioSegment> SynthesizeStreamingAsync(
        IAsyncEnumerable<string> textStream,
        KokoroVoiceOptions?      options           = null,
        CancellationToken        cancellationToken = default);
}