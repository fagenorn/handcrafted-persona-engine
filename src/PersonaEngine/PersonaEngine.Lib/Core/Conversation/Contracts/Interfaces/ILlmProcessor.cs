using PersonaEngine.Lib.Core.Conversation.Context;
using PersonaEngine.Lib.LLM;

namespace PersonaEngine.Lib.Core.Conversation.Contracts.Interfaces;

/// <summary>
///     Processes text input using an LLM, returning a stream of response chunks.
/// </summary>
public interface ILlmProcessor
{
    /// <summary>
    ///     Gets a streaming chat response from the LLM based on the current message and context.
    /// </summary>
    /// <param name="currentMessage">The current user message (utterance).</param>
    /// <param name="contextSnapshot">The snapshot of the conversation context (history, visual info, etc.).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    ///     An async enumerable yielding response text chunks.
    ///     Returns null or throws OperationCanceledException on failure/cancellation.
    /// </returns>
    IAsyncEnumerable<string>? GetStreamingChatResponseAsync(
        ChatMessage        currentMessage,
        LlmContextSnapshot contextSnapshot,
        CancellationToken  cancellationToken);
}