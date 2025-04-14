using PersonaEngine.Lib.LLM;

namespace PersonaEngine.Lib.Core.Conversation.Contracts.Interfaces;

/// <summary>
///     Processes text input using an LLM, returning a stream of response chunks.
/// </summary>
public interface ILlmProcessor
{
    /// <summary>
    ///     Gets a streaming chat response from the LLM.
    /// </summary>
    /// <param name="message">The user message (utterance).</param>
    /// <param name="metadata">Contextual metadata for the LLM.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    ///     An async enumerable yielding response text chunks.
    ///     Returns null or throws OperationCanceledException on failure/cancellation.
    /// </returns>
    IAsyncEnumerable<string>? GetStreamingChatResponseAsync(
        ChatMessage       message,
        InjectionMetadata metadata,
        CancellationToken cancellationToken);
}