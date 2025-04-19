using System.Diagnostics;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;
using PersonaEngine.Lib.Logging;

using Polly;
using Polly.Registry;

namespace PersonaEngine.Lib.LLM;

public class SemanticKernelChatEngine : IChatEngine
{
    private readonly IChatCompletionService _chatCompletionService;

    private readonly ILogger<SemanticKernelChatEngine> _logger;

    private readonly ResiliencePipeline _resiliencePipeline;

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public SemanticKernelChatEngine(
        Kernel                            kernel,
        ILogger<SemanticKernelChatEngine> logger, ResiliencePipelineProvider<string> resiliencePipelineProvider)
    {
        _logger                = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline    = resiliencePipelineProvider.GetPipeline("semantickernel-chat");
        _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>("text");

        _logger.LogInformation("SemanticKernelChatEngine initialized successfully");
    }

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
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        var  completedReason = CompletionReason.Completed;
        var  firstChunk      = true;
        var  stopwatch       = Stopwatch.StartNew();
        long chunkCount      = 0;

        try
        {
            var history = context.GetSemanticKernelChatHistory();

            var rc = ResilienceContextPool.Shared.Get(cancellationToken);
            rc.Properties.Set(ResilienceKeys.SessionId, sessionId);
            rc.Properties.Set(ResilienceKeys.Logger, _logger);

            await _resiliencePipeline.ExecuteAsync(async (x, token) =>
                                                   {
                                                       firstChunk = true;
                                                       chunkCount = 0;
                                                       x.stopwatch.Restart();

                                                       var streamingResponse = _chatCompletionService.GetStreamingChatMessageContentsAsync(
                                                                                                                                           x.history,
                                                                                                                                           x.executionSettings,
                                                                                                                                           null,
                                                                                                                                           token);

                                                       await foreach ( var chunk in streamingResponse.ConfigureAwait(false) )
                                                       {
                                                           chunkCount++;
                                                           var content = chunk.Content ?? string.Empty;

                                                           if ( firstChunk )
                                                           {
                                                               var firstChunkEvent = new LlmStreamStartEvent(x.sessionId, x.turnId, DateTimeOffset.UtcNow);
                                                               await x.outputWriter.WriteAsync(firstChunkEvent, token).ConfigureAwait(false);
                                                               firstChunk = false;
                                                           }

                                                           var chunkEvent = new LlmChunkEvent(x.sessionId, x.turnId, DateTimeOffset.UtcNow, content);
                                                           await x.outputWriter.WriteAsync(chunkEvent, token).ConfigureAwait(false);

                                                           _logger.LogTrace("Received chunk {ChunkNumber}: {ChunkLength} characters for TurnId: {TurnId}", chunkCount, content.Length, x.turnId);
                                                       }

                                                       _logger.LogInformation("Chat response streaming completed. Total chunks: {ChunkCount}, Total time: {ElapsedMs}ms", chunkCount, stopwatch.ElapsedMilliseconds);
                                                   }, (stopwatch, outputWriter, history, executionSettings, sessionId, turnId), cancellationToken).ConfigureAwait(false);

            ResilienceContextPool.Shared.Return(rc);

            stopwatch.Stop();

            if ( !firstChunk )
            {
                _logger.LogInformation(
                                       "Chat response streaming completed successfully for TurnId: {TurnId}. Total chunks: {ChunkCount}, Total time: {ElapsedMs}ms",
                                       turnId, chunkCount, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogWarning("Chat response streaming finished for TurnId: {TurnId}, but no chunks were received (potentially empty response or immediate failure). Time: {ElapsedMs}ms", turnId, stopwatch.ElapsedMilliseconds);
            }
        }
        catch (OperationCanceledException)
        {
            completedReason = CompletionReason.Cancelled;
            stopwatch.Stop();
        }
        catch (Exception ex)
        {
            completedReason = CompletionReason.Error;
            stopwatch.Stop();
            await outputWriter.WriteAsync(new ErrorOutputEvent(sessionId, turnId, DateTimeOffset.UtcNow, ex), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if ( !firstChunk )
            {
                var endEvent = new LlmStreamEndEvent(sessionId, turnId, DateTimeOffset.UtcNow, completedReason);
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await outputWriter.WriteAsync(endEvent, cts.Token).ConfigureAwait(false);
                }
                catch (Exception finalEx)
                {
                    _logger.LogError(finalEx, "Failed to write LlmStreamEndEvent for TurnId: {TurnId}", turnId);
                }
            }

            _semaphore.Release();
        }

        return completedReason;
    }
}