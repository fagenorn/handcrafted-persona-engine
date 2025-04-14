using System.Threading.Channels;

using PersonaEngine.Lib.Core.Conversation.Contracts.Events;

namespace PersonaEngine.Lib.Core.Conversation.Common.Messaging;

/// <summary>
///     Interface for accessing shared communication channels.
/// </summary>
public interface IChannelRegistry
{
    /// <summary>
    ///     Gets the channel dedicated to publishing and consuming transcription events.
    ///     (both potential and final).
    /// </summary>
    Channel<ITranscriptionEvent> TranscriptionEvents { get; }

    /// <summary>
    ///     Gets the channel dedicated to publishing and consuming completed user utterance events.
    /// </summary>
    Channel<UserUtteranceCompleted> UtteranceCompletionEvents { get; }

    /// <summary>
    ///     Gets the channel for publishing and consuming system state events,
    ///     like assistant speaking status.
    /// </summary>
    Channel<object> SystemStateEvents { get; }

    /// <summary>
    ///     Gets the channel for publishing and consuming barge-in detection events.
    /// </summary>
    Channel<BargeInDetected> BargeInEvents { get; }
}