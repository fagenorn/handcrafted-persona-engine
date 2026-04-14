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

    public VisualQASemanticKernelChatEngine(Kernel kernel, ILogger<VisualQASemanticKernelChatEngine> logger)
    {
        _logger                = logger ?? throw new ArgumentNullException(nameof(logger));
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
        VisualChatMessage                          visualInput,
        PromptExecutionSettings?                   executionSettings = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            var chatHistory = new ChatHistory("You are a helpful assistant.");
            chatHistory.AddUserMessage(
            [
                new TextContent(visualInput.Query),
                new ImageContent(visualInput.ImageData, "image/png")
            ]);

            var chunkCount = 0;
            var inThinkBlock = false;

            var streamingResponse = _chatCompletionService.GetStreamingChatMessageContentsAsync(
                                                                                                chatHistory,
                                                                                                executionSettings,
                                                                                                null,
                                                                                                cancellationToken);

            await foreach ( var chunk in streamingResponse.ConfigureAwait(false) )
            {
                cancellationToken.ThrowIfCancellationRequested();

                chunkCount++;
                var content = chunk.Content ?? string.Empty;
                var filtered = StripThinkTags(content, ref inThinkBlock);
                if ( filtered.Length == 0 ) continue;

                yield return filtered;
            }

            _logger.LogInformation("Visual QA response streaming completed. Total chunks: {ChunkCount}", chunkCount);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static string StripThinkTags(string input, ref bool inThinkBlock)
    {
        if ( input.Length == 0 ) return input;

        var output = new System.Text.StringBuilder(input.Length);
        var i = 0;
        while ( i < input.Length )
        {
            if ( inThinkBlock )
            {
                var close = input.IndexOf("</think>", i, StringComparison.OrdinalIgnoreCase);
                if ( close < 0 ) return output.ToString();
                i = close + "</think>".Length;
                inThinkBlock = false;
            }
            else
            {
                var open = input.IndexOf("<think>", i, StringComparison.OrdinalIgnoreCase);
                if ( open < 0 )
                {
                    output.Append(input, i, input.Length - i);
                    return output.ToString();
                }
                output.Append(input, i, open - i);
                i = open + "<think>".Length;
                inThinkBlock = true;
            }
        }
        return output.ToString();
    }
}