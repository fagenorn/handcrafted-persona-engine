using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

using OpenAI.Chat;

using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;

namespace PersonaEngine.Lib.LLM;

public class SemanticKernelChatEngine : IChatEngine
{
    private readonly IChatCompletionService _chatCompletionService;

    private readonly ILogger<SemanticKernelChatEngine> _logger;

    private readonly SemaphoreSlim _semaphore = new(1, 1);

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

    public async Task<CompletionReason> GetStreamingChatResponseAsync(
        IConversationContext        context,
        ChannelWriter<IOutputEvent> outputWriter,
        Guid                        turnId,
        Guid                        sessionId,
        PromptExecutionSettings?    executionSettings = null,
        CancellationToken           cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        var completedReason = CompletionReason.Completed;
        var firstChunk      = true;

        try
        {
            HistoryManager.Clear();

            var history = context.GetProjectedHistory();
            foreach ( var historyItem in history )
            {
                foreach ( var participantResponse in historyItem.ParticipantResponses )
                {
                    var input = participantResponse.Value;
                    if ( input.Role == ChatMessageRole.Assistant )
                    {
                        HistoryManager.AddAssistantMessage($"{input.FinalText}");
                    }
                    else
                    {
                        HistoryManager.AddUserMessage($"[{context.Participants[participantResponse.Key].Name}]{input.FinalText}");
                    }
                }
            }

            // if ( injectionMetadata != null )
            // {
            //     var injectionJson = CreateInjectionJson(injectionMetadata);
            //     if ( _metadataMsgId.HasValue && historyManager.UpdateMessage(_metadataMsgId.Value, injectionJson) )
            //     {
            //         // Update successful
            //     }
            //     else
            //     {
            //         _metadataMsgId = historyManager.AddUserMessage(injectionJson);
            //     }
            // }

            var stopwatch = Stopwatch.StartNew();

            var chunkCount = 0;

            var streamingResponse = _chatCompletionService.GetStreamingChatMessageContentsAsync(
                                                                                                HistoryManager.ChatHistory,
                                                                                                executionSettings,
                                                                                                null,
                                                                                                cancellationToken);

            await foreach ( var chunk in streamingResponse.ConfigureAwait(false) )
            {
                cancellationToken.ThrowIfCancellationRequested();

                chunkCount++;
                var content = chunk.Content ?? string.Empty;

                if ( firstChunk )
                {
                    var firstChunkEvent = new LlmStreamStartEvent(sessionId, turnId, DateTimeOffset.UtcNow);
                    await outputWriter.WriteAsync(firstChunkEvent, cancellationToken).ConfigureAwait(false);

                    firstChunk = false;
                }

                var chunkEvent = new LlmChunkEvent(sessionId, turnId, DateTimeOffset.UtcNow, content);
                await outputWriter.WriteAsync(chunkEvent, cancellationToken).ConfigureAwait(false);

                _logger.LogTrace("Received chunk {ChunkNumber}: {ChunkLength} characters", chunkCount, content.Length);
            }

            stopwatch.Stop();
            _logger.LogInformation("Chat response streaming completed. Total chunks: {ChunkCount}, Total time: {ElapsedMs}ms", chunkCount, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            completedReason = CompletionReason.Cancelled;
        }
        catch (Exception ex)
        {
            completedReason = CompletionReason.Error;

            await outputWriter.WriteAsync(new ErrorOutputEvent(sessionId, turnId, DateTimeOffset.UtcNow, ex), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if ( !firstChunk )
            {
                await outputWriter.WriteAsync(new LlmStreamEndEvent(sessionId, turnId, DateTimeOffset.UtcNow, completedReason), cancellationToken).ConfigureAwait(false);
            }

            _semaphore.Release();
        }

        return completedReason;
    }

    /// <summary>
    ///     Creates a JSON string for metadata injection.
    /// </summary>
    private static string CreateInjectionJson(InjectionMetadata metadata) { return JsonSerializer.Serialize(new { topics = metadata.Topics, context = metadata.Context, visual_context = metadata.VisualContext }); }
}