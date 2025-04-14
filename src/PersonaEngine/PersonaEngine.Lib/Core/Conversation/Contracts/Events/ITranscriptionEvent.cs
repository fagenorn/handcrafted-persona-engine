namespace PersonaEngine.Lib.Core.Conversation.Contracts.Events;

/// <summary>
///     Base interface for transcription-related events.
/// </summary>
public interface ITranscriptionEvent
{
    string SourceId { get; }

    DateTimeOffset Timestamp { get; }
}