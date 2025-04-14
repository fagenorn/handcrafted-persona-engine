using System.Threading.Channels;

using PersonaEngine.Lib.Core.Conversation.Contracts.Events;

namespace PersonaEngine.Lib.Core.Conversation.Common.Messaging;

/// <summary>
///     A simple implementation of IChannelRegistry using bounded channels.
/// </summary>
public sealed class ChannelRegistry : IChannelRegistry
{
    public Channel<ITranscriptionEvent> TranscriptionEvents { get; } =
        Channel.CreateBounded<ITranscriptionEvent>(new BoundedChannelOptions(100) { SingleReader = false, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });

    public Channel<UserUtteranceCompleted> UtteranceCompletionEvents { get; } =
        Channel.CreateBounded<UserUtteranceCompleted>(new BoundedChannelOptions(50) { SingleReader = false, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
}