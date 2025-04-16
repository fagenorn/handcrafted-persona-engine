using System.Threading.Channels;

using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;

public interface IOutputAdapter : IAsyncDisposable
{
    Guid AdapterId { get; }

    ValueTask InitializeAsync(Guid sessionId, CancellationToken cancellationToken);

    ValueTask StartAsync(CancellationToken cancellationToken);

    ValueTask StopAsync(CancellationToken cancellationToken);
}

public interface IAudioOutputAdapter : IOutputAdapter
{
    Task SendAsync(
        ChannelReader<TtsChunkEvent> inputReader,
        ChannelWriter<IOutputEvent>  outputWriter,
        Guid                         turnId,
        CancellationToken            cancellationToken = default);
}

public interface ITextOutputAdapter : IOutputAdapter
{
    Task SendAsync(
        ChannelReader<IOutputEvent> inputReader,
        ChannelWriter<IOutputEvent> outputWriter,
        Guid                        turnId,
        CancellationToken           cancellationToken = default);
}