using System.Text;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Core.Conversation.Common.Messaging;
using PersonaEngine.Lib.Core.Conversation.Contracts.Events;
using PersonaEngine.Lib.Core.Conversation.Contracts.Interfaces;
using PersonaEngine.Lib.LLM;
using PersonaEngine.Lib.Vision;

namespace PersonaEngine.Lib.Core.Conversation;

public class ConversationOrchestrator : IConversationOrchestrator
{
    private readonly IChannelRegistry _channelRegistry;

    private readonly string _llmContext = "Relaxed discussion in discord voice chat.";

    private readonly ILlmProcessor _llmProcessor;

    private readonly Lock _lock = new();

    private readonly ILogger<ConversationOrchestrator> _logger;

    private readonly ChannelWriter<ProcessOutputRequest> _outputRequestWriter;

    private readonly List<string> _topics = ["casual conversation"];

    private readonly IVisualQAService _visualQaService;

    private CancellationTokenSource? _activeProcessingCts = null;

    private Task? _activeProcessingTask = null;

    private State _currentState = State.Idle;

    public ConversationOrchestrator(
        IChannelRegistry                  channelRegistry,
        ILlmProcessor                     llmProcessor,
        IVisualQAService                  visualQaService,
        ILogger<ConversationOrchestrator> logger)
    {
        _channelRegistry     = channelRegistry;
        _llmProcessor        = llmProcessor;
        _visualQaService     = visualQaService;
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

    private Task OnUserUtteranceCompletedAsync(UserUtteranceCompleted utterance)
    {
        _logger.LogInformation("Orchestrator received utterance from {User}.", utterance.User);
        lock (_lock)
        {
            if ( _currentState == State.Idle )
            {
                if ( !string.IsNullOrWhiteSpace(utterance.AggregatedText) )
                {
                    TransitionTo(State.Processing);
                    _ = ProcessUtteranceFlowAsync(utterance);
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

        return Task.CompletedTask;
    }

    private Task OnBargeInDetectedAsync(BargeInDetected bargeInEvent)
    {
        _logger.LogInformation("Orchestrator received barge-in from {SourceId}.", bargeInEvent.SourceId);
        lock (_lock)
        {
            if ( _currentState == State.Streaming )
            {
                TransitionTo(State.Cancelling);
                _ = CancelCurrentActivityAsync($"Barge-in detected from {bargeInEvent.SourceId}");
            }
            else
            {
                _logger.LogWarning("Barge-in detected but state is {State}. Ignoring.", _currentState);
            }
        }

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
        Task?                    flowTask   = null; // Task representing the whole flow (LLM + publishing)

        lock (_lock)
        {
            if ( _currentState != State.Processing )
            {
                _logger.LogWarning("ProcessUtteranceFlowAsync started but state changed to {State}. Aborting.", _currentState);

                return;
            }

            _activeProcessingCts?.Dispose();
            _activeProcessingCts  = new CancellationTokenSource();
            currentCts            = _activeProcessingCts;
            _activeProcessingTask = null; // Placeholder, actual task assigned below
        }

        _logger.LogInformation("Starting LLM processing flow for utterance from {User} (SourceId: {SourceId}).", utterance.User, utterance.SourceId);
        var cancellationToken = currentCts.Token;

        try
        {
            // --- Create the actual processing task ---
            flowTask = Task.Run(async () =>
                                {
                                    // Encapsulate the logic in a task
                                    IAsyncEnumerable<string>? llmStream          = null;
                                    string?                   aggregatedResponse = null;

                                    try
                                    {
                                        // 1. Get LLM Response Stream
                                        var message = new ChatMessage(utterance.User, utterance.AggregatedText);
                                        var metadata = new InjectionMetadata(
                                                                             _topics.AsReadOnly(),
                                                                             _llmContext,
                                                                             _visualQaService.ScreenCaption ?? string.Empty);

                                        llmStream = _llmProcessor.GetStreamingChatResponseAsync(message, metadata, cancellationToken);

                                        if ( llmStream == null )
                                        {
                                            _logger.LogWarning("LLM Processor returned null stream.");

                                            throw new InvalidOperationException("LLM processing failed to return a stream.");
                                        }

                                        // 2. Aggregate LLM Response (Simple aggregation for Phase 1)
                                        _logger.LogDebug("Aggregating LLM response stream...");
                                        var responseBuilder = new StringBuilder();
                                        await foreach ( var chunk in llmStream.WithCancellation(cancellationToken) )
                                        {
                                            cancellationToken.ThrowIfCancellationRequested(); // Check frequently during aggregation
                                            responseBuilder.Append(chunk);
                                        }

                                        aggregatedResponse = responseBuilder.ToString();
                                        _logger.LogInformation("LLM response aggregated (Length: {Length}).", aggregatedResponse.Length);

                                        if ( string.IsNullOrWhiteSpace(aggregatedResponse) )
                                        {
                                            _logger.LogWarning("LLM returned empty or whitespace response.");

                                            // Decide whether to publish an empty request or just transition to Idle
                                            // Let's not publish anything for an empty response.
                                            return; // Exit the task successfully
                                        }

                                        // 3. Publish Output Request
                                        _logger.LogDebug("Publishing ProcessOutputRequest...");
                                        var outputRequest = new ProcessOutputRequest(
                                                                                     aggregatedResponse,
                                                                                     utterance.SourceId // Pass originating SourceId for context
                                                                                     // Add other metadata if needed, e.g., utterance start time
                                                                                     // Metadata = new Dictionary<string, object> { { "UtteranceStartTime", utterance.StartTimestamp } }
                                                                                    );

                                        // Check for cancellation *before* writing to the channel
                                        cancellationToken.ThrowIfCancellationRequested();

                                        await _outputRequestWriter.WriteAsync(outputRequest, cancellationToken);
                                        _logger.LogDebug("ProcessOutputRequest published.");
                                    }
                                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                                    {
                                        _logger.LogInformation("LLM/Aggregation/Publishing task cancelled.");

                                        // Let the exception propagate to the outer catch block of ProcessUtteranceFlowAsync
                                        throw;
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Error during LLM/Aggregation/Publishing task.");

                                        // Let the exception propagate
                                        throw;
                                    }
                                }, cancellationToken); // Pass token to Task.Run

            // --- Assign task under lock ---
            lock (_lock)
            {
                // Check if state changed or cancelled while task was being set up
                if ( _activeProcessingCts != currentCts || cancellationToken.IsCancellationRequested )
                {
                    _logger.LogWarning("State or cancellation changed during flow task setup. Aborting assignment.");
                    // Cancel the newly created task if it wasn't already cancelled
                    if ( !cancellationToken.IsCancellationRequested )
                    {
                        currentCts.Cancel();
                    }

                    flowTask = null; // Don't track it
                    // Manually transition back to Idle if needed? The finally block should handle this.
                }
                else
                {
                    _activeProcessingTask = flowTask; // Track the flow task
                }
            }

            // --- Await the flow task completion (outside lock) ---
            if ( flowTask != null )
            {
                _logger.LogDebug("Awaiting LLM/Aggregation/Publishing task...");
                await flowTask; // Await the task completion

                // If we reach here without exceptions or cancellation, it completed naturally
                var flowCompletedSuccessfully = !cancellationToken.IsCancellationRequested;
                _logger.LogInformation("LLM/Aggregation/Publishing flow completed. Success: {Success}", flowCompletedSuccessfully);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("LLM/Aggregation/Publishing flow was cancelled (outer catch).");
            // State transition handled by finally block or AssistantSpeakingStopped(Cancelled) if audio started somehow (it shouldn't now)
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during LLM/Aggregation/Publishing flow (outer catch).");
            // State transition handled by finally block or AssistantSpeakingStopped(Error)
        }
        finally
        {
            lock (_lock)
            {
                if ( _activeProcessingCts == currentCts )
                {
                    ClearActiveTask("Flow finally block");
                    // Ensure transition back to Idle happens reliably.
                    // Since output is now decoupled, we transition to Idle once the request is published (or fails).
                    // AssistantSpeaking events are now just informational for barge-in.
                    if ( _currentState == State.Processing ) // Should transition from Processing
                    {
                        _logger.LogDebug("Transitioning to Idle in flow finally block.");
                        TransitionTo(State.Idle);
                    }
                    else if ( _currentState == State.Cancelling )
                    {
                        // If cancellation happened, ensure we end up Idle.
                        _logger.LogDebug("Transitioning to Idle after cancellation in flow finally block.");
                        TransitionTo(State.Idle);
                    }
                    else
                    {
                        _logger.LogWarning("In finally block, state was {State}, expected Processing or Cancelling. Forcing Idle.", _currentState);
                        TransitionTo(State.Idle); // Force idle state if something went wrong
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

    private enum State
    {
        Idle,

        Processing,

        Streaming,

        Cancelling
    }
}