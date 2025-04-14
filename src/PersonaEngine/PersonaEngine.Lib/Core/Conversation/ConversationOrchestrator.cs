using System.Text;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using OpenAI.Chat;

using PersonaEngine.Lib.Core.Conversation.Common.Messaging;
using PersonaEngine.Lib.Core.Conversation.Context;
using PersonaEngine.Lib.Core.Conversation.Contracts.Events;
using PersonaEngine.Lib.Core.Conversation.Contracts.Interfaces;
using PersonaEngine.Lib.Core.Conversation.Policies;

using Stateless;
// Added for FSM
using ChatMessage = PersonaEngine.Lib.LLM.ChatMessage;

namespace PersonaEngine.Lib.Core.Conversation;

public class ConversationOrchestrator : IConversationOrchestrator
{
    // Define states for the conversation flow
    public enum State
    {
        Idle,

        Processing, // Processing LLM response, potentially waiting for output adapters

        Cancelling // Actively cancelling the Processing state
    }

    private readonly IChannelRegistry _channelRegistry;

    private readonly IContextManager _contextManager;

    private readonly ILlmProcessor _llmProcessor;

    private readonly ILogger<ConversationOrchestrator> _logger;

    private readonly IOutputFormattingStrategy _outputFormatter;

    private readonly ChannelWriter<ProcessOutputRequest> _outputRequestWriter;

    // --- State related to the active processing task ---
    private readonly object _processingLock = new(); // Lock specifically for CTS/Task management

    // Triggers with parameters need to be pre-configured
    private readonly StateMachine<State, Trigger>.TriggerWithParameters<UtteranceTriggerParameter> _receiveUtteranceTrigger;

    private readonly StateMachine<State, Trigger>.TriggerWithParameters<CancellationTriggerParameter> _requestCancellationTrigger;

    // The Finite State Machine instance
    private readonly StateMachine<State, Trigger> _stateMachine;

    private readonly ITurnTakingStrategy _turnTakingStrategy;

    private CancellationTokenSource? _activeProcessingCts = null;

    private Task? _activeProcessingTask = null;

    private UserUtteranceCompleted? _currentProcessingUtterance = null; // Store utterance being processed
    // ----------------------------------------------------

    private CancellationTokenSource? _orchestratorLoopCts = null; // For the main event loop

    public ConversationOrchestrator(
        IChannelRegistry                  channelRegistry,
        ILlmProcessor                     llmProcessor,
        IContextManager                   contextManager,
        ILogger<ConversationOrchestrator> logger,
        IOutputFormattingStrategy         outputFormatter,
        ITurnTakingStrategy               turnTakingStrategy) // Make sure this is correctly registered in DI
    {
        // Add null checks for critical dependencies in constructor
        _channelRegistry     = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
        _llmProcessor        = llmProcessor ?? throw new ArgumentNullException(nameof(llmProcessor));
        _contextManager      = contextManager ?? throw new ArgumentNullException(nameof(contextManager));
        _logger              = logger ?? throw new ArgumentNullException(nameof(logger));
        _outputFormatter     = outputFormatter ?? throw new ArgumentNullException(nameof(outputFormatter));
        _turnTakingStrategy  = turnTakingStrategy ?? throw new ArgumentNullException(nameof(turnTakingStrategy));
        _outputRequestWriter = channelRegistry.OutputRequests?.Writer ?? throw new ArgumentNullException(nameof(channelRegistry.OutputRequests.Writer));

        // --- Initialize State Machine ---
        _stateMachine = new StateMachine<State, Trigger>(State.Idle);

        // Configure triggers with parameters
        _receiveUtteranceTrigger    = _stateMachine.SetTriggerParameters<UtteranceTriggerParameter>(Trigger.ReceiveUtterance);
        _requestCancellationTrigger = _stateMachine.SetTriggerParameters<CancellationTriggerParameter>(Trigger.RequestCancellation);

        // --- Configure States ---

        _stateMachine.Configure(State.Idle)
                     .PermitIf(_receiveUtteranceTrigger, State.Processing, ShouldProcessUtteranceGuard)
                     .Ignore(Trigger.DetectBargeIn)
                     .Ignore(Trigger.ProcessingComplete)
                     .Ignore(Trigger.ProcessingFailed)
                     .Ignore(Trigger.RequestCancellation)
                     .Ignore(Trigger.CancellationComplete);

        _stateMachine.Configure(State.Processing)
                     .OnEntryFromAsync(_receiveUtteranceTrigger, StartProcessingFlowAsync)
                     .Permit(Trigger.ProcessingComplete, State.Idle)
                     .Permit(Trigger.ProcessingFailed, State.Idle)
                     .Permit(Trigger.DetectBargeIn, State.Cancelling)
                     .Permit(_requestCancellationTrigger.Trigger, State.Cancelling)
                     .OnExitAsync(EnsureProcessingFlowIsSignalledToCancelAsync)
                     .Ignore(Trigger.ReceiveUtterance)
                     .Ignore(Trigger.CancellationComplete);

        _stateMachine.Configure(State.Cancelling)
                     .OnEntryFromAsync(Trigger.DetectBargeIn, () => StartCancellationFlowAsync("Barge-in detected"))
                     .OnEntryFromAsync(_requestCancellationTrigger, param => StartCancellationFlowAsync(param.Reason))
                     .Permit(Trigger.CancellationComplete, State.Idle)
                     .Ignore(Trigger.ReceiveUtterance)
                     .Ignore(Trigger.DetectBargeIn)
                     .Ignore(Trigger.RequestCancellation)
                     .Ignore(Trigger.ProcessingComplete)
                     .Ignore(Trigger.ProcessingFailed);

        _stateMachine.OnTransitioned(t => _logger.LogInformation("FSM Transition: {Source} -> {Destination} via {Trigger}", t.Source, t.Destination, t.Trigger));

        // --- End State Machine Configuration ---
    }

    // --- Service Lifecycle Methods ---

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Conversation Orchestrator (FSM)...");
        _orchestratorLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _                    = ProcessEventsAsync(_orchestratorLoopCts.Token);
        _logger.LogInformation("Conversation Orchestrator (FSM) started.");

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping Conversation Orchestrator (FSM)...");

        if ( _orchestratorLoopCts != null && !_orchestratorLoopCts.IsCancellationRequested )
        {
            _logger.LogDebug("Cancelling orchestrator event loop CTS.");
            await _orchestratorLoopCts.CancelAsync();
        }

        var cancelParam   = new CancellationTriggerParameter("Orchestrator stopping");
        var canFireCancel = false;
        // Add null check for _stateMachine here
        if ( _stateMachine != null && _stateMachine.CanFire(_requestCancellationTrigger.Trigger) )
        {
            canFireCancel = true;
        }

        if ( canFireCancel )
        {
            _logger.LogDebug("Firing RequestCancellation trigger during stop.");
            try
            {
                await _stateMachine.FireAsync(_requestCancellationTrigger, cancelParam);
                await Task.Delay(200);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error firing RequestCancellation trigger during stop. Attempting direct cancel.");
                await CancelActiveProcessingTaskInternalAsync("Orchestrator stopping (FSM fire failed)");
            }
        }
        else
        {
            _logger.LogDebug("Cannot fire RequestCancellation trigger during stop (current state: {State}). Signalling internal cancel directly.", _stateMachine?.State);
            await CancelActiveProcessingTaskInternalAsync("Orchestrator stopping");
            await Task.Delay(200);
        }

        _logger.LogInformation("Conversation Orchestrator (FSM) stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing Conversation Orchestrator (FSM)...");
        await StopAsync();
        lock (_processingLock)
        {
            _activeProcessingCts?.Dispose();
            _activeProcessingCts = null;
        }

        _orchestratorLoopCts?.Dispose();
        _logger.LogInformation("Conversation Orchestrator (FSM) disposed.");
    }

    // Guard condition for processing an utterance - Added null checks
    private bool ShouldProcessUtteranceGuard(UtteranceTriggerParameter param)
    {
        // --- Start Null Checks ---
        // Removed specific param == null check as FireAsync should guarantee it now.
        // Keep checks for dependencies and nested properties.
        if ( _turnTakingStrategy == null )
        {
            _logger.LogError("TurnTakingStrategy is null in ShouldProcessUtteranceGuard. Cannot decide action.");

            return false;
        }

        if ( param?.Utterance == null ) // Still check param and nested Utterance defensively
        {
            Console.Error.WriteLine("[ERROR] UtteranceTriggerParameter or its Utterance is null in ShouldProcessUtteranceGuard.");
            _logger?.LogError("UtteranceTriggerParameter or its Utterance is null in ShouldProcessUtteranceGuard.");

            return false;
        }

        if ( _stateMachine == null )
        {
            _logger?.LogError("StateMachine is null in ShouldProcessUtteranceGuard.");

            return false;
        }
        // --- End Null Checks ---

        var action = _turnTakingStrategy.DecideAction(param.Utterance, _stateMachine.State);
        _logger?.LogDebug("Turn-taking strategy decided action: {Action} for utterance from {User}", action, param.Utterance.User);

        var shouldProcess = action == TurnTakingAction.ProcessNow && !string.IsNullOrWhiteSpace(param.Utterance.AggregatedText);

        if ( !shouldProcess && action == TurnTakingAction.ProcessNow )
        {
            _logger?.LogWarning("Ignoring empty utterance even though strategy allowed processing.");
        }
        else if ( action == TurnTakingAction.Ignore )
        {
            _logger?.LogInformation("Strategy requested ignoring utterance from {User} (SourceId: {SourceId}) because current state is {State}.", param.Utterance.User, param.Utterance.SourceId, _stateMachine.State);
        }
        else if ( action == TurnTakingAction.Queue )
        {
            _logger?.LogWarning("Strategy requested queuing utterance from {User} (SourceId: {SourceId}), but queuing is not implemented. Ignoring.", param.Utterance.User, param.Utterance.SourceId);
        }

        return shouldProcess;
    }

    // Action executed when entering the Processing state
    private async Task StartProcessingFlowAsync(UtteranceTriggerParameter param)
    {
        if ( param?.Utterance == null )
        {
            _logger.LogError("UtteranceTriggerParameter or Utterance is null in StartProcessingFlowAsync.");
            // Ensure state machine exists before firing
            if ( _stateMachine != null )
            {
                await _stateMachine.FireAsync(Trigger.ProcessingFailed);
            }

            return;
        }

        var utterance = param.Utterance;
        _logger.LogInformation("FSM: Entering Processing state for utterance from {User} (SourceId: {SourceId}).", utterance.User, utterance.SourceId);

        CancellationTokenSource processingCts;
        Task                    processingTask;

        lock (_processingLock)
        {
            _activeProcessingCts?.Dispose();
            _activeProcessingCts        = new CancellationTokenSource();
            processingCts               = _activeProcessingCts;
            _currentProcessingUtterance = utterance;

            _activeProcessingTask = Task.Run(async () =>
                                             {
                                                 var success = false;
                                                 var taskId  = Task.CurrentId ?? -1;
                                                 try
                                                 {
                                                     _logger.LogDebug("Starting LLM processing task (Task ID: {TaskId})...", taskId);
                                                     await ExecuteLlmProcessingWorkflowAsync(utterance, processingCts.Token);
                                                     if ( !processingCts.Token.IsCancellationRequested )
                                                     {
                                                         success = true;
                                                         _logger.LogDebug("LLM processing task (Task ID: {TaskId}) completed successfully.", taskId);
                                                     }
                                                     else
                                                     {
                                                         _logger.LogInformation("LLM processing task (Task ID: {TaskId}) was cancelled during execution.", taskId);
                                                     }
                                                 }
                                                 catch (OperationCanceledException) when (processingCts.IsCancellationRequested)
                                                 {
                                                     _logger.LogInformation("LLM processing task (Task ID: {TaskId}) cancelled.", taskId);
                                                 }
                                                 catch (Exception ex)
                                                 {
                                                     _logger.LogError(ex, "Error during LLM processing task (Task ID: {TaskId}).", taskId);
                                                     success = false;
                                                 }
                                                 finally
                                                 {
                                                     if ( !processingCts.IsCancellationRequested )
                                                     {
                                                         _logger.LogDebug("LLM processing task (Task ID: {TaskId}) finished naturally. Firing completion/failure trigger.", taskId);
                                                         var trigger = success ? Trigger.ProcessingComplete : Trigger.ProcessingFailed;
                                                         try
                                                         {
                                                             if ( _stateMachine != null )
                                                             {
                                                                 await _stateMachine.FireAsync(trigger);
                                                             }
                                                             else
                                                             {
                                                                 _logger.LogWarning("State machine was null when trying to fire completion/failure trigger for Task ID {TaskId}.", taskId);
                                                             }
                                                         }
                                                         catch (Exception fireEx)
                                                         {
                                                             _logger.LogError(fireEx, "Error firing FSM trigger {Trigger} for Task ID {TaskId}", trigger, taskId);
                                                         }
                                                     }
                                                     else
                                                     {
                                                         _logger.LogDebug("LLM processing task (Task ID: {TaskId}) finished due to cancellation. No completion/failure trigger fired.", taskId);
                                                     }

                                                     lock (_processingLock)
                                                     {
                                                         if ( _activeProcessingTask?.Id == taskId )
                                                         {
                                                             _logger.LogDebug("Clearing active task/CTS reference for completed/cancelled task (Task ID: {TaskId}).", taskId);
                                                             _activeProcessingTask = null;
                                                             _activeProcessingCts?.Dispose();
                                                             _activeProcessingCts        = null;
                                                             _currentProcessingUtterance = null;
                                                         }
                                                         else
                                                         {
                                                             _logger.LogWarning("Completed/cancelled task (Task ID: {TaskId}) tried to clear references, but a different task (ID: {ActiveTaskId}) is now active or none is active.", taskId, _activeProcessingTask?.Id);
                                                             try
                                                             {
                                                                 processingCts.Dispose();
                                                             }
                                                             catch
                                                             {
                                                                 /* Ignore */
                                                             }
                                                         }
                                                     }
                                                 }
                                             }, processingCts.Token);

            processingTask = _activeProcessingTask;
        }

        _logger.LogDebug("Created LLM processing task (Task ID: {TaskId}).", processingTask?.Id ?? -1);

        await Task.CompletedTask;
    }

    // The actual steps of getting LLM response and publishing output request
    private async Task ExecuteLlmProcessingWorkflowAsync(UserUtteranceCompleted utterance, CancellationToken cancellationToken)
    {
        if ( utterance == null )
        {
            throw new ArgumentNullException(nameof(utterance), "Utterance cannot be null in ExecuteLlmProcessingWorkflowAsync");
        }

        IAsyncEnumerable<string>? llmStream          = null;
        string?                   aggregatedResponse = null;

        // 1. Get Context Snapshot
        _logger.LogDebug("Getting context snapshot...");
        var contextSnapshot = await _contextManager.GetContextSnapshotAsync(utterance.SourceId);
        cancellationToken.ThrowIfCancellationRequested();

        // 2. Get LLM Response Stream
        _logger.LogDebug("Requesting LLM stream...");
        var currentMessage = new ChatMessage(utterance.User, utterance.AggregatedText);
        llmStream = _llmProcessor.GetStreamingChatResponseAsync(currentMessage, contextSnapshot, cancellationToken);

        if ( llmStream == null )
        {
            _logger.LogWarning("LLM Processor returned null stream.");

            throw new InvalidOperationException("LLM processing failed to return a stream.");
        }

        // 3. Aggregate LLM Response
        _logger.LogDebug("Aggregating LLM response stream...");
        var responseBuilder = new StringBuilder();
        await foreach ( var chunk in llmStream.WithCancellation(cancellationToken) )
        {
            responseBuilder.Append(chunk);
        }

        cancellationToken.ThrowIfCancellationRequested();

        aggregatedResponse = responseBuilder.ToString();
        _logger.LogInformation("LLM response aggregated (Length: {Length}).", aggregatedResponse.Length);

        if ( string.IsNullOrWhiteSpace(aggregatedResponse) )
        {
            _logger.LogWarning("LLM returned empty or whitespace response. Completing successfully without output.");

            return;
        }

        // 4. Format Response
        string formattedResponse;
        try
        {
            _logger.LogDebug("Applying output formatting strategy...");
            formattedResponse = _outputFormatter.Format(aggregatedResponse);
            _logger.LogDebug("Output formatting applied.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying output formatting strategy. Using raw response.");
            formattedResponse = aggregatedResponse;
        }

        // 5. Publish Output Request
        _logger.LogDebug("Publishing ProcessOutputRequest...");
        var outputRequest = new ProcessOutputRequest(
                                                     formattedResponse,
                                                     utterance.SourceId
                                                    );

        cancellationToken.ThrowIfCancellationRequested();
        await _outputRequestWriter.WriteAsync(outputRequest, cancellationToken);
        _logger.LogDebug("ProcessOutputRequest published.");

        // 6. Add Assistant Interaction to Context
        var assistantInteraction = new Interaction(
                                                   "Assistant",
                                                   ChatMessageRole.Assistant,
                                                   aggregatedResponse,
                                                   DateTimeOffset.Now
                                                  );

        try
        {
            await _contextManager.AddInteractionAsync(assistantInteraction);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add assistant interaction to context history.");
        }
    }

    // Action executed when leaving the Processing state
    private async Task EnsureProcessingFlowIsSignalledToCancelAsync(StateMachine<State, Trigger>.Transition transition)
    {
        _logger.LogDebug("FSM: Exiting Processing state via trigger {Trigger}. Ensuring active flow is cancelled.", transition.Trigger);
        _ = CancelActiveProcessingTaskInternalAsync($"Exiting Processing state due to {transition.Trigger}");
        await Task.CompletedTask;
    }

    // Action executed when entering the Cancelling state
    private async Task StartCancellationFlowAsync(string reason)
    {
        _logger.LogInformation("FSM: Entering Cancelling state. Reason: {Reason}", reason);

        Task? taskToAwait   = null;
        int?  taskToAwaitId = null;
        lock (_processingLock)
        {
            taskToAwait   = _activeProcessingTask;
            taskToAwaitId = taskToAwait?.Id;
        }

        if ( taskToAwait != null && !taskToAwait.IsCompleted )
        {
            _logger.LogDebug("Waiting briefly for active task ({TaskId}) to observe cancellation...", taskToAwaitId);
            try
            {
                await Task.WhenAny(taskToAwait, Task.Delay(TimeSpan.FromSeconds(1)));
                _logger.LogDebug("Wait completed. Task Status: {Status}", taskToAwait.Status);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception during wait for cancelled task in Cancelling state.");
            }
        }
        else
        {
            _logger.LogDebug("No active task or task already completed when entering Cancelling state (Task ID: {TaskId}).", taskToAwaitId);
        }

        _logger.LogDebug("Cancellation flow finished. Firing CancellationComplete trigger.");
        try
        {
            if ( _stateMachine != null )
            {
                await _stateMachine.FireAsync(Trigger.CancellationComplete);
            }
            else
            {
                _logger.LogWarning("State machine was null when trying to fire CancellationComplete trigger.");
            }
        }
        catch (Exception fireEx)
        {
            _logger.LogError(fireEx, "Error firing FSM trigger CancellationComplete");
        }
    }

    // Internal helper to signal cancellation to the active task CTS
    private async Task CancelActiveProcessingTaskInternalAsync(string reason)
    {
        CancellationTokenSource? ctsToCancel = null;
        int?                     taskId      = null;
        lock (_processingLock)
        {
            ctsToCancel = _activeProcessingCts;
            taskId      = _activeProcessingTask?.Id;
        }

        if ( ctsToCancel != null && !ctsToCancel.IsCancellationRequested )
        {
            _logger.LogInformation("Requesting cancellation of active processing task (Task ID: {TaskId}). Reason: {Reason}", taskId, reason);
            try
            {
                await ctsToCancel.CancelAsync();
                _logger.LogDebug("Cancellation signal sent to active processing task's (Task ID: {TaskId}) CTS.", taskId);
            }
            catch (ObjectDisposedException)
            {
                _logger.LogWarning("Attempted to cancel an already disposed CTS (Task ID: {TaskId}).", taskId);
                lock (_processingLock)
                {
                    if ( _activeProcessingCts == ctsToCancel )
                    {
                        _activeProcessingCts        = null;
                        _activeProcessingTask       = null;
                        _currentProcessingUtterance = null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while cancelling CTS (Task ID: {TaskId}).", taskId);
            }
        }
        else if ( ctsToCancel == null )
        {
            _logger.LogDebug("Attempted to cancel active processing task, but no CTS was found (perhaps already completed/cancelled).");
        }
        else
        {
            _logger.LogDebug("Attempted to cancel active processing task (Task ID: {TaskId}), but cancellation was already requested.", taskId);
        }
    }

    // --- Event Processing Loop ---

    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting orchestrator event processing loop.");

        var utteranceReader = _channelRegistry.UtteranceCompletionEvents.Reader;
        var bargeInReader   = _channelRegistry.BargeInEvents.Reader;

        var channels = new List<Task> { ReadChannelLoopAsync(utteranceReader, OnUserUtteranceCompletedInternalAsync, cancellationToken), ReadChannelLoopAsync(bargeInReader, OnBargeInDetectedInternalAsync, cancellationToken) };

        try
        {
            await Task.WhenAll(channels);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Orchestrator event loop cancelled via token.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in orchestrator event processing Task.WhenAll.");
        }
        finally
        {
            _logger.LogInformation("Orchestrator event loop finished.");
        }
    }

    // Helper to continuously read from a channel and process items
    private async Task ReadChannelLoopAsync<T>(ChannelReader<T> reader, Func<T, Task> processor, CancellationToken ct)
    {
        try
        {
            await foreach ( var item in reader.ReadAllAsync(ct) )
            {
                try
                {
                    if ( ct.IsCancellationRequested )
                    {
                        _logger.LogDebug("Cancellation requested, skipping processing of item {ItemType}.", typeof(T).Name);

                        continue;
                    }

                    if ( item == null )
                    {
                        _logger.LogWarning("Read a null item from channel {ItemType}.", typeof(T).Name);

                        continue;
                    }

                    await processor(item);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing item of type {ItemType} in ReadChannelLoopAsync.", typeof(T).Name);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Channel reading loop cancelled for type {ItemType}.", typeof(T).Name);
        }
        catch (ChannelClosedException)
        {
            _logger.LogInformation("Channel closed for type {ItemType}.", typeof(T).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in channel reading loop for type {ItemType}.", typeof(T).Name);
        }
        finally
        {
            _logger.LogDebug("Exiting channel reading loop for type {ItemType}.", typeof(T).Name);
        }
    }

    // Internal handler called by the channel loop
    private async Task OnUserUtteranceCompletedInternalAsync(UserUtteranceCompleted utterance)
    {
        if ( utterance == null )
        {
            _logger.LogWarning("Received null UserUtteranceCompleted event.");

            return;
        }

        _logger.LogInformation("Orchestrator received utterance from {User} (SourceId: {SourceId}). Text: '{Text}'",
                               utterance.User, utterance.SourceId, utterance.AggregatedText);

        await AddUserInteractionToContextAsync(utterance);

        var triggerParam = new UtteranceTriggerParameter(utterance);
        _logger.LogTrace("Constructed UtteranceTriggerParameter. Utterance content: '{Content}'", triggerParam.Utterance.AggregatedText);

        if ( _stateMachine == null )
        {
            _logger.LogError("State machine is not initialized.");

            return;
        }

        _logger.LogDebug("Attempting to fire ReceiveUtterance trigger directly...");
        try
        {
            await _stateMachine.FireAsync(_receiveUtteranceTrigger, triggerParam);
            _logger.LogDebug("Successfully fired ReceiveUtterance trigger.");
        }
        catch (InvalidOperationException ioex)
        {
            _logger.LogWarning(ioex, "ReceiveUtterance trigger cannot be fired in current state ({State}). Utterance ignored by FSM.", _stateMachine.State);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error firing ReceiveUtterance trigger or executing its guard/action.");
        }
    }

    // Helper to add interaction safely
    private async Task AddUserInteractionToContextAsync(UserUtteranceCompleted utterance)
    {
        if ( utterance == null )
        {
            return;
        }

        var userInteraction = new Interaction(
                                              utterance.SourceId,
                                              ChatMessageRole.User,
                                              utterance.AggregatedText,
                                              utterance.EndTimestamp
                                             );

        try
        {
            if ( _contextManager != null )
            {
                await _contextManager.AddInteractionAsync(userInteraction);
            }
            else
            {
                _logger.LogError("ContextManager is null, cannot add user interaction.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add user interaction to context history.");
        }
    }

    // Internal handler called by the channel loop
    private async Task OnBargeInDetectedInternalAsync(BargeInDetected bargeInEvent)
    {
        if ( bargeInEvent == null )
        {
            _logger.LogWarning("Received null BargeInDetected event.");

            return;
        }

        _logger.LogInformation("Orchestrator received barge-in from {SourceId}.", bargeInEvent.SourceId);

        if ( _stateMachine == null )
        {
            _logger.LogError("State machine is not initialized.");

            return;
        }

        _logger.LogDebug("Attempting to fire DetectBargeIn trigger directly...");
        try
        {
            await _stateMachine.FireAsync(Trigger.DetectBargeIn);
            _logger.LogDebug("Successfully fired DetectBargeIn trigger.");
        }
        catch (InvalidOperationException ioex)
        {
            _logger.LogWarning(ioex, "DetectBargeIn trigger cannot be fired in current state ({State}). Barge-in ignored.", _stateMachine.State);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error firing DetectBargeIn trigger.");
        }
    }

    // Define triggers that cause state transitions
    private enum Trigger
    {
        ReceiveUtterance, // User utterance completed

        DetectBargeIn, // User started speaking while assistant was speaking/processing

        RequestCancellation, // Explicit external request to stop current activity

        ProcessingComplete, // LLM/Output flow finished successfully

        ProcessingFailed, // LLM/Output flow failed

        CancellationComplete // Cancellation process finished
    }

    // Parameter classes for triggers that need to pass data
    private class UtteranceTriggerParameter(UserUtteranceCompleted utterance)
    {
        public UserUtteranceCompleted Utterance { get; } = utterance;
    }

    private class CancellationTriggerParameter(string reason)
    {
        public string Reason { get; } = reason;
    }
}