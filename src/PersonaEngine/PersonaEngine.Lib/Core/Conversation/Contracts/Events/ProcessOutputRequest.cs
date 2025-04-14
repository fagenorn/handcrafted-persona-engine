namespace PersonaEngine.Lib.Core.Conversation.Contracts.Events;

/// <summary>
///     Represents a request for an output adapter to process and render content.
/// </summary>
/// <param name="RequestId">A unique identifier for this specific output request.</param>
/// <param name="Content">The text content to output.</param>
/// <param name="TargetSourceId">Optional: ID of the input source this output is responding to (for context/routing).</param>
/// <param name="TargetChannelId">Optional: Specific channel/destination ID (e.g., Discord channel).</param>
/// <param name="OutputTypeHint">Optional: Hint for adapters (e.g., "Audio", "Text", "Action"). Defaults to "Default".</param>
/// <param name="Metadata">Optional: Extra contextual information.</param>
public record ProcessOutputRequest(
    Guid RequestId,
    string                               Content,
    string?                              TargetSourceId  = null,
    string?                              TargetChannelId = null,
    string                               OutputTypeHint  = "Default",
    IReadOnlyDictionary<string, object>? Metadata        = null
);