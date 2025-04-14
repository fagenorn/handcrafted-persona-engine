using System.Threading.Channels;

using PersonaEngine.Lib.Core.Conversation.Contracts.Events;

namespace PersonaEngine.Lib.Core.Conversation.Common.Messaging;

/// <summary>
/// A simple implementation of IChannelRegistry using bounded channels.
/// </summary>
public sealed class ChannelRegistry : IChannelRegistry
{
    // Using a base interface or object allows one channel for related events,
    // consumers can filter by type. Adjust capacity as needed.
    public Channel<ITranscriptionEvent> TranscriptionEvents { get; } =
        Channel.CreateBounded<ITranscriptionEvent>(new BoundedChannelOptions(100)
                                                   {
                                                       // Allow multiple readers if different services need to react
                                                       SingleReader = false,
                                                       // Typically only one writer (the source service)
                                                       SingleWriter = true,
                                                       FullMode     = BoundedChannelFullMode.Wait
                                                   });
}