using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Core.Conversation.Context;
using PersonaEngine.Lib.Core.Conversation.Contracts.Interfaces;
using PersonaEngine.Lib.LLM;

namespace PersonaEngine.Lib.Core.Conversation;

public class LlmProcessor(IChatEngine llmEngine, ILogger<LlmProcessor> logger) : ILlmProcessor
{
    public IAsyncEnumerable<string>? GetStreamingChatResponseAsync(
        ChatMessage        currentMessage,
        LlmContextSnapshot contextSnapshot,
        CancellationToken  cancellationToken)
    {
        logger.LogDebug("Requesting LLM stream for user: {User}", currentMessage.User);
        try
        {
            // *** TODO: Adapt the call to the underlying llmEngine ***
            // The IChatEngine interface and its implementation will need to be updated
            // to accept the LlmContextSnapshot (or relevant parts like history)
            // instead of, or in addition to, InjectionMetadata.

            // Placeholder showing how the call *might* look after adapting IChatEngine:
            // return llmEngine.GetStreamingChatResponseAsync(currentMessage, contextSnapshot, cancellationToken);

            // --- TEMPORARY / EXAMPLE ADAPTATION ---
            // If IChatEngine still expects InjectionMetadata, we need to map back:
            logger.LogWarning("TODO: IChatEngine needs update. Mapping LlmContextSnapshot back to InjectionMetadata temporarily.");
            var tempMetadata = new InjectionMetadata(
                                                     contextSnapshot.Topics?.ToList() ?? new List<string>(),
                                                     contextSnapshot.SystemContext ?? string.Empty,
                                                     contextSnapshot.ScreenCaption ?? string.Empty
                                                     // Note: History from contextSnapshot is not passed via InjectionMetadata here.
                                                     // The IChatEngine needs to be updated to handle history properly.
                                                    );

            return llmEngine.GetStreamingChatResponseAsync(currentMessage, tempMetadata, cancellationToken: cancellationToken);
            // --- END TEMPORARY ADAPTATION ---
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("LLM stream request cancelled.");

            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error requesting LLM stream.");

            throw;
        }
    }
}