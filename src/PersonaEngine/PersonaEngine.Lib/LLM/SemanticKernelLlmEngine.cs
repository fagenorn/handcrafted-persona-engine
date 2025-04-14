using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace PersonaEngine.Lib.LLM;

public class SemanticKernelChatEngine : IChatEngine
{
    private readonly IChatCompletionService _chatCompletionService;

    private readonly ILogger<SemanticKernelChatEngine> _logger;

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private Guid? _metadataMsgId;

    public SemanticKernelChatEngine(
        Kernel                            kernel,
        IChatHistoryManager               historyManager,
        ILogger<SemanticKernelChatEngine> logger)
    {
        _logger                = logger ?? throw new ArgumentNullException(nameof(logger));
        _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>("text");
        HistoryManager         = historyManager ?? throw new ArgumentNullException(nameof(historyManager));

        _logger.LogInformation("SemanticKernelChatEngine initialized successfully");
    }

    public IChatHistoryManager HistoryManager { get; }

    public void Dispose()
    {
        try
        {
            _logger.LogInformation("Disposing SemanticKernelChatEngine");
            _semaphore.Dispose();
            GC.SuppressFinalize(this);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while disposing SemanticKernelChatEngine");
        }
    }

    public IAsyncEnumerable<string> GetStreamingChatResponseAsync(
        ChatMessage              userInput,
        InjectionMetadata?       injectionMetadata = null,
        PromptExecutionSettings? executionSettings = null,
        CancellationToken        cancellationToken = default)
    {
        return GetStreamingChatResponseWithHistoryAsync(userInput, HistoryManager, injectionMetadata, executionSettings, cancellationToken);
    }

    /// <summary>
    ///     Gets a streaming chat response using the provided chat history manager.
    ///     This allows using a specific history context for the chat (e.g., for per-user history in Discord).
    /// </summary>
    public async IAsyncEnumerable<string> GetStreamingChatResponseWithHistoryAsync(
        ChatMessage                                userInput,
        IChatHistoryManager                        historyManager,
        InjectionMetadata?                         injectionMetadata = null,
        PromptExecutionSettings?                   executionSettings = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);

        if ( injectionMetadata != null )
        {
            var injectionJson = CreateInjectionJson(injectionMetadata);
            if ( _metadataMsgId.HasValue && historyManager.UpdateMessage(_metadataMsgId.Value, injectionJson) )
            {
                // Update successful
            }
            else
            {
                _metadataMsgId = historyManager.AddUserMessage(injectionJson);
            }
        }

        var userMsgId = historyManager.AddUserMessage($"[{userInput.User}]{userInput.Content.Trim()}");

        var cleanupNeeded = true;
        var stopwatch     = Stopwatch.StartNew();

        try
        {
            var chunkCount         = 0;
            var rawResponseBuilder = new StringBuilder();
            var pendingBuffer      = string.Empty;
            var prefixProcessed    = false;

            var streamingResponse = _chatCompletionService.GetStreamingChatMessageContentsAsync(
                                                                                                historyManager.ChatHistory,
                                                                                                executionSettings,
                                                                                                null,
                                                                                                cancellationToken);

            await foreach ( var chunk in streamingResponse.ConfigureAwait(false) )
            {
                cancellationToken.ThrowIfCancellationRequested();

                chunkCount++;
                var content = chunk.Content ?? string.Empty;
                rawResponseBuilder.Append(content);

                if ( !prefixProcessed )
                {
                    pendingBuffer += content;

                    if ( pendingBuffer.Length > 0 && pendingBuffer[0] == '[' )
                    {
                        var closingBracketIndex = pendingBuffer.IndexOf(']');
                        if ( closingBracketIndex < 0 )
                        {
                            continue;
                        }

                        pendingBuffer   = pendingBuffer[(closingBracketIndex + 1)..];
                        prefixProcessed = true;
                        if ( string.IsNullOrEmpty(pendingBuffer) )
                        {
                            continue;
                        }

                        _logger.LogTrace("Received chunk {ChunkNumber}: {ChunkLength} characters", chunkCount, pendingBuffer.Length);

                        yield return pendingBuffer;
                        pendingBuffer = string.Empty;
                    }
                    else
                    {
                        _logger.LogTrace("Received chunk {ChunkNumber}: {ChunkLength} characters", chunkCount, pendingBuffer.Length);

                        yield return pendingBuffer;
                        pendingBuffer = string.Empty;
                    }
                }
                else
                {
                    _logger.LogTrace("Received chunk {ChunkNumber}: {ChunkLength} characters", chunkCount, content.Length);

                    yield return content;
                }
            }

            historyManager.AddAssistantMessage(rawResponseBuilder.ToString());
            stopwatch.Stop();
            _logger.LogInformation("Chat response streaming completed. Total chunks: {ChunkCount}, Total time: {ElapsedMs}ms", chunkCount, stopwatch.ElapsedMilliseconds);

            cleanupNeeded = false;
        }
        finally
        {
            if ( cleanupNeeded )
            {
                _logger.LogWarning("Cleaning up user message due to cancellation or error.");
                historyManager.RemoveMessage(userMsgId);
            }

            _semaphore.Release();
        }
    }

    /// <summary>
    ///     Creates a JSON string for metadata injection.
    /// </summary>
    private static string CreateInjectionJson(InjectionMetadata metadata) { return JsonSerializer.Serialize(new { topics = metadata.Topics, context = metadata.Context, visual_context = metadata.VisualContext }); }
}