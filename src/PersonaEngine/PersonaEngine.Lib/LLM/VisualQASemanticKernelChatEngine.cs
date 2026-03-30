using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace PersonaEngine.Lib.LLM;

public class VisualQASemanticKernelChatEngine : IVisualChatEngine
{
    private readonly IChatCompletionService _chatCompletionService;

    private readonly ILogger<VisualQASemanticKernelChatEngine> _logger;

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public VisualQASemanticKernelChatEngine(
        Kernel kernel,
        ILogger<VisualQASemanticKernelChatEngine> logger
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>("vision");
    }

    public void Dispose()
    {
        try
        {
            _logger.LogInformation("Disposing VisualQASemanticKernelChatEngine");
            _semaphore.Dispose();
            GC.SuppressFinalize(this);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while disposing VisualQASemanticKernelChatEngine");
        }
    }

    public async IAsyncEnumerable<string> GetStreamingChatResponseAsync(
        VisualChatMessage visualInput,
        PromptExecutionSettings? executionSettings = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            var chatHistory = new ChatHistory("You are a helpful assistant.");
            chatHistory.AddUserMessage(
                [
                    new TextContent(visualInput.Query),
                    new ImageContent(visualInput.ImageData, "image/png"),
                ]
            );

            var chunkCount = 0;

            var streamingResponse = _chatCompletionService.GetStreamingChatMessageContentsAsync(
                chatHistory,
                executionSettings,
                null,
                cancellationToken
            );

            await foreach (var chunk in streamingResponse.ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                chunkCount++;
                var content = chunk.Content ?? string.Empty;

                yield return content;
            }

            _logger.LogInformation(
                "Visual QA response streaming completed. Total chunks: {ChunkCount}",
                chunkCount
            );
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
