using System.Threading.Channels;

using PersonaEngine.Lib.Core.Conversation.Contracts.Events;

namespace PersonaEngine.Lib.Core.Conversation.Common.Messaging;

/// <summary>
/// Interface for accessing shared communication channels.
/// </summary>
public interface IChannelRegistry
{
    /// <summary>
    /// Gets the channel dedicated to publishing and consuming transcription events.
    /// </summary>
    Channel<ITranscriptionEvent> TranscriptionEvents { get; }
    
    // Add other channels here as needed for different event types
}