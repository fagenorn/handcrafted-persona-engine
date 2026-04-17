using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using PersonaEngine.Lib.LLM.Connection;

namespace PersonaEngine.Lib.LLM;

public class VisualQASemanticKernelChatEngine : IVisualChatEngine
{
    private readonly Lazy<ILlmKernelProvider> _kernelProvider;

    private readonly ILogger<VisualQASemanticKernelChatEngine> _logger;

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public VisualQASemanticKernelChatEngine(
        Lazy<ILlmKernelProvider> kernelProvider,
        ILogger<VisualQASemanticKernelChatEngine> logger
    )
    {
        _kernelProvider = kernelProvider ?? throw new ArgumentNullException(nameof(kernelProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            var chatCompletionService =
                _kernelProvider.Value.Current.GetRequiredService<IChatCompletionService>("vision");
            var chatHistory = new ChatHistory("You are a helpful assistant.");
            chatHistory.AddUserMessage(
                [
                    new TextContent(visualInput.Query),
                    new ImageContent(visualInput.ImageData, "image/png"),
                ]
            );

            var chunkCount = 0;

            var streamingResponse = chatCompletionService.GetStreamingChatMessageContentsAsync(
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
