using System.Threading.Channels;

using PersonaEngine.Lib.Audio;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;

public interface IInputAdapter : IAsyncDisposable
{
    Guid AdapterId { get; }
    
    ParticipantInfo Participant { get; }

    ValueTask InitializeAsync(Guid sessionId, ChannelWriter<IInputEvent> inputWriter, CancellationToken cancellationToken);

    ValueTask StartAsync(CancellationToken cancellationToken);

    ValueTask StopAsync(CancellationToken cancellationToken);
}

public interface IAudioInputAdapter : IInputAdapter
{
    IAwaitableAudioSource GetAudioSource();
}

public interface ITextInputAdapter : IInputAdapter { }