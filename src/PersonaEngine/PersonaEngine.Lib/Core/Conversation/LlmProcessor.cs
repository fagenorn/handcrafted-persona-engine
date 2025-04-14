using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Core.Conversation.Contracts.Interfaces;
using PersonaEngine.Lib.LLM;

namespace PersonaEngine.Lib.Core.Conversation;

public class LlmProcessor(IChatEngine llmEngine, ILogger<LlmProcessor> logger) : ILlmProcessor
{
    public IAsyncEnumerable<string>? GetStreamingChatResponseAsync(
        ChatMessage       message,
        InjectionMetadata metadata,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Requesting LLM stream for user: {User}", message.User);
        try
        {
            // Directly call the underlying engine
            // The engine itself should handle internal cancellation checks
            return llmEngine.GetStreamingChatResponseAsync(message, metadata, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("LLM stream request cancelled.");

            // Return null or let the exception propagate? Propagating might be clearer.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error requesting LLM stream.");

            // Propagate exception to let the orchestrator handle it
            throw;
        }
    }

    // Note: Cancellation is handled via the CancellationToken passed into GetStreamingChatResponseAsync.
    // No separate Cancel() method needed on this interface/implementation currently.
}