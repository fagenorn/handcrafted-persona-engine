using System.Text;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using OpenAI.Chat;

using PersonaEngine.Lib.Core.Conversation.Common.Messaging;
using PersonaEngine.Lib.Core.Conversation.Context;
using PersonaEngine.Lib.Core.Conversation.Contracts.Events;
using PersonaEngine.Lib.Core.Conversation.Contracts.Interfaces;
using PersonaEngine.Lib.LLM;
using PersonaEngine.Lib.Vision;

using ChatMessage = PersonaEngine.Lib.LLM.ChatMessage;

namespace PersonaEngine.Lib.Core.Conversation;

public class ConversationOrchestrator : IConversationOrchestrator
{
    private readonly IChannelRegistry _channelRegistry;
    
    private readonly ILlmProcessor _llmProcessor;

    private readonly IContextManager _contextManager;
    
    private readonly Lock            _lock = new();

    private readonly ILogger<ConversationOrchestrator> _logger;

    private readonly ChannelWriter<ProcessOutputRequest> _outputRequestWriter;
    
    private CancellationTokenSource? _activeProcessingCts = null;

    private Task? _activeProcessingTask = null;

    private State _currentState = State.Idle;

    public ConversationOrchestrator(
        IChannelRegistry                  channelRegistry,
        ILlmProcessor                     llmProcessor,
        IContextManager                   contextManager,
        ILogger<ConversationOrchestrator> logger)
    {
        _channelRegistry = channelRegistry;
        _llmProcessor    = llmProcessor;
        _contextManager  = contextManager;
        _logger              = logger;
        _outputRequestWriter = channelRegistry.OutputRequests.Writer;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Conversation Orchestrator...");
        _ = ProcessEventsAsync(cancellationToken);
        _logger.LogInformation("Conversation Orchestrator started.");

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping Conversation Orchestrator...");
        await CancelCurrentActivityAsync("Orchestrator stopping");
        _logger.LogInformation("Conversation Orchestrator stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing Conversation Orchestrator...");
        await StopAsync();
        _activeProcessingCts?.Dispose();
        _logger.LogInformation("Conversation Orchestrator disposed.");
    }

    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting orchestrator event processing loop.");

        var utteranceReader   = _channelRegistry.UtteranceCompletionEvents.Reader;
        var bargeInReader     = _channelRegistry.BargeInEvents.Reader;
        var systemStateReader = _channelRegistry.SystemStateEvents.Reader;

        var utteranceTask   = ReadChannelAsync(utteranceReader, cancellationToken);
        var bargeInTask     = ReadChannelAsync(bargeInReader, cancellationToken);
        var systemStateTask = ReadChannelAsync(systemStateReader, cancellationToken);

        var tasks = new List<Task> { utteranceTask, bargeInTask, systemStateTask };

        try
        {
            while ( !cancellationToken.IsCancellationRequested && tasks.Count > 0 )
            {
                var completedTask = await Task.WhenAny(tasks);

                if ( completedTask == utteranceTask )
                {
                    var (utterance, completed) = await utteranceTask;
                    if ( utterance != null )
                    {
                        await OnUserUtteranceCompletedAsync(utterance);
                    }

                    tasks.Remove(utteranceTask);
                    if ( completed )
                    {
                        utteranceTask = null;
                    }
                    else
                    {
                        utteranceTask = ReadChannelAsync(utteranceReader, cancellationToken);
                        tasks.Add(utteranceTask);
                    }
                }
                else if ( completedTask == bargeInTask )
                {
                    var (bargeIn, completed) = await bargeInTask;
                    if ( bargeIn != null )
                    {
                        await OnBargeInDetectedAsync(bargeIn);
                    }

                    tasks.Remove(bargeInTask);
                    if ( completed )
                    {
                        bargeInTask = null;
                    }
                    else
                    {
                        bargeInTask = ReadChannelAsync(bargeInReader, cancellationToken);
                        tasks.Add(bargeInTask);
                    }
                }
                else if ( completedTask == systemStateTask )
                {
                    var (stateEvent, completed) = await systemStateTask;
                    if ( stateEvent != null )
                    {
                        await OnSystemStateEventAsync(stateEvent);
                    }

                    tasks.Remove(systemStateTask);
                    if ( completed )
                    {
                        systemStateTask = null;
                    }
                    else
                    {
                        systemStateTask = ReadChannelAsync(systemStateReader, cancellationToken);
                        tasks.Add(systemStateTask);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Orchestrator event loop cancelled via token.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in orchestrator event loop.");
        }
        finally
        {
            _logger.LogInformation("Orchestrator event loop finished.");
        }
    }

    private static async Task<(T? item, bool completed)> ReadChannelAsync<T>(ChannelReader<T> reader, CancellationToken ct) where T : class
    {
        try
        {
            if ( await reader.WaitToReadAsync(ct) )
            {
                if ( reader.TryRead(out var item) )
                {
                    return (item, false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            /* Expected */
        }
        catch (ChannelClosedException)
        {
            /* Expected */
        }

        return (null, true);
    }

    private async Task OnUserUtteranceCompletedAsync(UserUtteranceCompleted utterance)
    {
        _logger.LogInformation("Orchestrator received utterance from {User} (SourceId: {SourceId}). Text: '{Text}'",
                               utterance.User, utterance.SourceId, utterance.AggregatedText);

        var userInteraction = new Interaction(
                                              utterance.SourceId,
                                              ChatMessageRole.User,
                                              utterance.AggregatedText,
                                              utterance.EndTimestamp
                                             );
        
        try
        {
            await _contextManager.AddInteractionAsync(userInteraction);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add user interaction to context history.");
            // Continue processing anyway? Yes.
        }
        
        lock (_lock)
        {
            if (_currentState == State.Idle)
            {
                if (!string.IsNullOrWhiteSpace(utterance.AggregatedText))
                {
                    TransitionTo(State.Processing);
                    // Pass the utterance object to the processing flow
                    _ = ProcessUtteranceFlowAsync(utterance); // Fire and forget the flow start
                }
                else
                {
                    _logger.LogWarning("Ignoring empty utterance.");
                }
            }
            else
            {
                // TODO: Implement queuing or interruption logic for utterances arriving while busy.
                _logger.LogWarning("Utterance received while orchestrator state is {State}. Ignoring.", _currentState);
            }
        }
    }

    private Task OnBargeInDetectedAsync(BargeInDetected bargeInEvent)
    {
        _logger.LogInformation("Orchestrator received barge-in from {SourceId}.", bargeInEvent.SourceId);
        // lock (_lock)
        // {
        //     if ( _currentState == State.Streaming )
        //     {
        //         TransitionTo(State.Cancelling);
        //         _ = CancelCurrentActivityAsync($"Barge-in detected from {bargeInEvent.SourceId}");
        //     }
        //     else
        //     {
        //         _logger.LogWarning("Barge-in detected but state is {State}. Ignoring.", _currentState);
        //     }
        // }

        return Task.CompletedTask;
    }

    private Task OnSystemStateEventAsync(object stateEvent)
    {
        lock (_lock)
        {
            switch ( stateEvent )
            {
                case AssistantSpeakingStarted started:
                    _logger.LogDebug("Orchestrator noted AssistantSpeakingStarted event from an adapter.");

                    // Don't transition Orchestrator state based on this anymore.
                    // Just potentially use this info for barge-in logic enablement (which BargeInDetector already does).
                    break;

                case AssistantSpeakingStopped stopped:
                    _logger.LogDebug("Orchestrator noted AssistantSpeakingStopped event from an adapter. Reason: {Reason}", stopped.Reason);

                    // Don't transition Orchestrator state based on this anymore.
                    // The flow reaches Idle when publishing is done or it fails/cancels.
                    break;
            }
        }

        return Task.CompletedTask;
    }

    private async Task ProcessUtteranceFlowAsync(UserUtteranceCompleted utterance)
    {
        CancellationTokenSource? currentCts = null;
        Task?                    flowTask   = null;
        

        lock (_lock) // Assign CTS under lock
        {
            if (_currentState != State.Processing)
            {
                _logger.LogWarning("ProcessUtteranceFlowAsync started but state changed to {State}. Aborting.", _currentState);
                return;
            }
            _activeProcessingCts?.Dispose();
            _activeProcessingCts  = new CancellationTokenSource();
            currentCts            = _activeProcessingCts;
            _activeProcessingTask = null; // Placeholder
        }

        _logger.LogInformation("Starting LLM processing flow for utterance from {User} (SourceId: {SourceId}).", utterance.User, utterance.SourceId);
        var cancellationToken = currentCts.Token;

         try
        {
            // --- Create the actual processing task ---
            flowTask = Task.Run(async () => {
                IAsyncEnumerable<string>? llmStream = null;
                string? aggregatedResponse = null;

                try
                {
                    // 1. Get Context Snapshot
                    _logger.LogDebug("Getting context snapshot...");
                    var contextSnapshot = await _contextManager.GetContextSnapshotAsync(utterance.SourceId);
                    cancellationToken.ThrowIfCancellationRequested(); // Check after async call

                    // 2. Get LLM Response Stream (using new signature)
                    _logger.LogDebug("Requesting LLM stream...");
                    var currentMessage = new ChatMessage(utterance.User, utterance.AggregatedText); // Current message remains separate

                    llmStream = _llmProcessor.GetStreamingChatResponseAsync(
                        currentMessage,
                        contextSnapshot, // Pass the snapshot
                        cancellationToken);

                    if (llmStream == null)
                    {
                        _logger.LogWarning("LLM Processor returned null stream.");
                        throw new InvalidOperationException("LLM processing failed to return a stream.");
                    }

                    // 3. Aggregate LLM Response
                    _logger.LogDebug("Aggregating LLM response stream...");
                    var responseBuilder = new StringBuilder();
                    await foreach (var chunk in llmStream.WithCancellation(cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        responseBuilder.Append(chunk);
                    }
                    aggregatedResponse = responseBuilder.ToString();
                    _logger.LogInformation("LLM response aggregated (Length: {Length}).", aggregatedResponse.Length);

                    if (string.IsNullOrWhiteSpace(aggregatedResponse))
                    {
                        _logger.LogWarning("LLM returned empty or whitespace response.");
                        return; // Exit task successfully, don't publish or add to history
                    }

                    // --- Add Assistant Interaction to Context ---
                    // Add *before* publishing, so if publishing fails, it's still recorded? Or after?
                    // Let's add *after* successful publishing, as the output wasn't fully delivered otherwise.
                    // Moved this logic after the WriteAsync call below.
                    // -------------------------------------------

                    // 4. Publish Output Request
                    _logger.LogDebug("Publishing ProcessOutputRequest...");
                    var outputRequest = new ProcessOutputRequest(
                        aggregatedResponse,
                        utterance.SourceId // Pass originating SourceId for context
                    );

                    cancellationToken.ThrowIfCancellationRequested();
                    await _outputRequestWriter.WriteAsync(outputRequest, cancellationToken);
                    _logger.LogDebug("ProcessOutputRequest published.");

                    // --- Add Assistant Interaction to Context (AFTER successful publish) ---
                    var assistantInteraction = new Interaction(
                        "Assistant", // Or a more specific bot ID if available
                        ChatMessageRole.Assistant, // Assuming Assistant role
                        aggregatedResponse,
                        DateTimeOffset.Now // Timestamp when response was finalized/published
                    );
                    try
                    {
                        await _contextManager.AddInteractionAsync(assistantInteraction);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to add assistant interaction to context history.");
                        // Non-fatal, continue flow
                    }
                    // -------------------------------------------

                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("LLM/Aggregation/Publishing task cancelled.");
                    throw; // Propagate
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during LLM/Aggregation/Publishing task.");
                    throw; // Propagate
                }

            }, cancellationToken);

            // --- Assign task under lock ---
            lock (_lock)
            {
                if (_activeProcessingCts != currentCts || cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("State or cancellation changed during flow task setup. Aborting assignment.");
                    if (!cancellationToken.IsCancellationRequested) currentCts.Cancel();
                    flowTask = null;
                }
                else
                {
                    _activeProcessingTask = flowTask;
                }
            }

            // --- Await the flow task completion (outside lock) ---
            if (flowTask != null)
            {
                _logger.LogDebug("Awaiting LLM/Aggregation/Publishing task...");
                await flowTask;
                var flowCompletedSuccessfully = !cancellationToken.IsCancellationRequested;
                _logger.LogInformation("LLM/Aggregation/Publishing flow completed. Success: {Success}", flowCompletedSuccessfully);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("LLM/Aggregation/Publishing flow was cancelled (outer catch).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during LLM/Aggregation/Publishing flow (outer catch).");
            // Consider adding an error interaction to context manager? Maybe not.
        }
        finally
        {
            lock (_lock)
            {
                if (_activeProcessingCts == currentCts)
                {
                    ClearActiveTask("Flow finally block");
                    if (_currentState == State.Processing || _currentState == State.Cancelling)
                    {
                        _logger.LogDebug("Transitioning to Idle in flow finally block (State: {CurrentState}).", _currentState);
                        TransitionTo(State.Idle);
                    }
                    else
                    {
                        _logger.LogWarning("In finally block, state was {State}, expected Processing or Cancelling. Forcing Idle.", _currentState);
                        TransitionTo(State.Idle);
                    }
                }
                else
                {
                    _logger.LogWarning("ProcessUtteranceFlowAsync finally: Active CTS changed, skipping cleanup for this flow.");
                }
            }
        }
    }

    private void TransitionTo(State newState)
    {
        // Assumes lock is held
        if ( _currentState == newState )
        {
            return;
        }

        _logger.LogInformation("Orchestrator state transition: {OldState} -> {NewState}", _currentState, newState);
        var oldState = _currentState;
        _currentState = newState;

        // State 'Streaming' is less relevant now for the orchestrator itself.
        // Might remove it later if AudioOutputAdapter handles all audio-related state.
        // Keep it for now, but don't transition *to* it from Processing.

        // Reset things when becoming Idle
        if ( newState == State.Idle && oldState != State.Idle )
        {
            ClearActiveTask("Transitioning to Idle");
        }
    }

    private async Task CancelCurrentActivityAsync(string reason)
    {
        _logger.LogInformation("Attempting to cancel current activity (LLM/Aggregation/Publishing flow). Reason: {Reason}", reason);
        CancellationTokenSource? ctsToCancel = null;
        Task?                    taskToAwait = null;

        lock (_lock)
        {
            // Can cancel during Processing state now
            if ( _currentState == State.Processing ) // Streaming state might become obsolete
            {
                // If already cancelling, do nothing more here
                if ( _currentState == State.Cancelling )
                {
                    return;
                }

                TransitionTo(State.Cancelling); // Move to cancelling state

                ctsToCancel = _activeProcessingCts;  // Get the CTS to cancel
                taskToAwait = _activeProcessingTask; // Get the task to potentially await

                // We no longer directly stop audio here. Adapters handle their cancellation.
                // The AssistantSpeakingStopped(Cancelled) event will come from the AudioOutputAdapter if it was playing.
            }
            else
            {
                _logger.LogWarning("Request to cancel activity, but state is {State}. Nothing to cancel.", _currentState);

                return; // Nothing to do
            }
        }

        // Perform cancellation outside the lock
        if ( ctsToCancel != null && !ctsToCancel.IsCancellationRequested )
        {
            _logger.LogDebug("Signalling cancellation token.");
            try
            {
                await ctsToCancel.CancelAsync(); // Use async version
            }
            catch (ObjectDisposedException)
            {
                /* Ignore if already disposed */
            }
        }

        // Await the task briefly to allow it to observe cancellation
        if ( taskToAwait != null && !taskToAwait.IsCompleted )
        {
            _logger.LogDebug("Waiting briefly for active task ({TaskId}) to observe cancellation...", taskToAwait.Id);
            try
            {
                await Task.WhenAny(taskToAwait, Task.Delay(TimeSpan.FromMilliseconds(200))); // Brief wait
                _logger.LogDebug("Brief wait completed. Task Status: {Status}", taskToAwait.Status);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception during brief wait for cancelled task.");
            }
        }

        // State should transition to Idle via the finally block of ProcessUtteranceFlowAsync
        // The failsafe task delay might still be useful.
        _ = Task.Delay(500).ContinueWith(_ =>
                                         {
                                             lock (_lock)
                                             {
                                                 if ( _currentState == State.Cancelling )
                                                 {
                                                     _logger.LogWarning("Forcing state to Idle after cancellation timeout.");
                                                     TransitionTo(State.Idle);
                                                 }
                                             }
                                         });
    }

    private void ClearActiveTask(string reason)
    {
        // Assumes lock is held
        if ( _activeProcessingTask != null || _activeProcessingCts != null )
        {
            _logger.LogDebug("Clearing active processing task/CTS. Reason: {Reason}", reason);
            _activeProcessingCts?.Dispose(); // Dispose the CTS
            _activeProcessingCts  = null;
            _activeProcessingTask = null;
        }
    }

    private enum State { Idle, Processing, Cancelling }
}