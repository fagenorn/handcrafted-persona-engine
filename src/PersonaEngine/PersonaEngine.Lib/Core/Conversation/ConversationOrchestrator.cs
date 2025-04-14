// ========================================================================
// FILE: src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/ConversationOrchestrator.cs
// REASON: Modified cancellation flow to automatically reprocess a new utterance
//         that arrives during Processing or WaitingForAudio states, after
//         cancelling the original request.
// ========================================================================
using System.Diagnostics;
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
// Required for List
// Required for DebuggerNonUserCode
// Added for FSM
using ChatMessage = PersonaEngine.Lib.LLM.ChatMessage; // Alias if needed

namespace PersonaEngine.Lib.Core.Conversation;

public class ConversationOrchestrator : IConversationOrchestrator
{
    // Define states for the conversation flow
    public enum State
    {
        Idle,
        Processing,           // Processing LLM request, preparing output request
        WaitingForAudio,      // Output request sent, waiting for AssistantSpeakingStarted event
        Speaking,             // Assistant audio is actively playing
        Cancelling            // Actively cancelling Processing, WaitingForAudio, or Speaking state
    }

    private readonly IAudioOutputService _audioOutputService; // Added dependency

    private readonly StateMachine<State, Trigger>.TriggerWithParameters<SystemEventTriggerParameter> _audioPlaybackStartedTrigger;
    private readonly StateMachine<State, Trigger>.TriggerWithParameters<SystemEventTriggerParameter> _audioPlaybackStoppedTrigger;

    private readonly ChannelReader<BargeInDetected> _bargeInReader;

    // --- Dependencies ---
    private readonly IChannelRegistry _channelRegistry;
    private readonly IContextManager _contextManager;
    private readonly ILlmProcessor _llmProcessor;
    private readonly ILogger<ConversationOrchestrator> _logger;
    private readonly IOutputFormattingStrategy _outputFormatter;
    private readonly ChannelWriter<ProcessOutputRequest> _outputRequestWriter;

    // --- State related to the active processing/speaking task ---
    private readonly object _processingLock = new(); // Lock specifically for CTS/Task/Request management
    private readonly StateMachine<State, Trigger>.TriggerWithParameters<UtteranceTriggerParameter> _receiveUtteranceTrigger;
    private readonly StateMachine<State, Trigger>.TriggerWithParameters<CancellationTriggerParameter> _requestCancellationTrigger;

    // --- State Machine ---
    private readonly StateMachine<State, Trigger> _stateMachine;
    private readonly ChannelReader<object> _systemStateReader; // Added reader for system events
    private readonly ITurnTakingStrategy _turnTakingStrategy;
    private readonly ChannelReader<UserUtteranceCompleted> _utteranceReader;

    private CancellationTokenSource? _activeProcessingCts = null; // CTS for the LLM/Request generation task
    private Task? _activeProcessingTask = null;
    private UserUtteranceCompleted? _currentProcessingUtterance = null; // Store utterance being processed
    private Guid _currentRequestId = Guid.Empty; // Store the ID of the current request being processed/spoken

    // --- Orchestrator Lifecycle ---
    private CancellationTokenSource? _orchestratorLoopCts = null; // For the main event loop

    // --- State for reprocessing after cancellation ---
    private UserUtteranceCompleted? _utteranceToReprocess = null;

    public ConversationOrchestrator(
        IChannelRegistry channelRegistry,
        ILlmProcessor llmProcessor,
        IContextManager contextManager,
        ILogger<ConversationOrchestrator> logger,
        IOutputFormattingStrategy outputFormatter,
        ITurnTakingStrategy turnTakingStrategy,
        IAudioOutputService audioOutputService) // Added dependency
    {
        // Null checks for dependencies
        _channelRegistry = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
        _llmProcessor = llmProcessor ?? throw new ArgumentNullException(nameof(llmProcessor));
        _contextManager = contextManager ?? throw new ArgumentNullException(nameof(contextManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _outputFormatter = outputFormatter ?? throw new ArgumentNullException(nameof(outputFormatter));
        _turnTakingStrategy = turnTakingStrategy ?? throw new ArgumentNullException(nameof(turnTakingStrategy));
        _audioOutputService = audioOutputService ?? throw new ArgumentNullException(nameof(audioOutputService)); // Added check

        // Channel Writers/Readers
        _outputRequestWriter = channelRegistry.OutputRequests?.Writer ?? throw new ArgumentNullException(nameof(channelRegistry.OutputRequests));
        _systemStateReader = channelRegistry.SystemStateEvents?.Reader ?? throw new ArgumentNullException(nameof(channelRegistry.SystemStateEvents)); // Added reader
        _utteranceReader = channelRegistry.UtteranceCompletionEvents?.Reader ?? throw new ArgumentNullException(nameof(channelRegistry.UtteranceCompletionEvents));
        _bargeInReader = channelRegistry.BargeInEvents?.Reader ?? throw new ArgumentNullException(nameof(channelRegistry.BargeInEvents));

        // --- Initialize State Machine ---
        _stateMachine = new StateMachine<State, Trigger>(State.Idle);

        // Configure triggers with parameters
        _receiveUtteranceTrigger = _stateMachine.SetTriggerParameters<UtteranceTriggerParameter>(Trigger.ReceiveUtterance);
        _requestCancellationTrigger = _stateMachine.SetTriggerParameters<CancellationTriggerParameter>(Trigger.RequestCancellation);
        _audioPlaybackStartedTrigger = _stateMachine.SetTriggerParameters<SystemEventTriggerParameter>(Trigger.AudioPlaybackStarted);
        _audioPlaybackStoppedTrigger = _stateMachine.SetTriggerParameters<SystemEventTriggerParameter>(Trigger.AudioPlaybackStopped);

        // --- Configure States ---

        _stateMachine.Configure(State.Idle)
            .PermitIf(_receiveUtteranceTrigger, State.Processing, ShouldProcessUtteranceGuard)
            // ** Add OnEntry action to check for and process a pending utterance **
            .OnEntryAsync(ProcessPendingUtteranceAsync)
            .Ignore(Trigger.DetectBargeIn)             // No active processing/speaking to interrupt
            .Ignore(Trigger.RequestCancellation)        // Nothing to cancel
            .Ignore(Trigger.OutputRequestPublished)
            .Ignore(Trigger.OutputRequestFailed)
            .Ignore(_audioPlaybackStartedTrigger.Trigger)
            .Ignore(_audioPlaybackStoppedTrigger.Trigger)
            .Ignore(Trigger.CancellationComplete); // Cancellation leads directly to Idle, then OnEntry handles reprocess

        _stateMachine.Configure(State.Processing)
            .OnEntryFromAsync(_receiveUtteranceTrigger, StartProcessingFlowAsync)
            // Explicitly permit transition to Cancelling on early barge-in
            .PermitIf(_receiveUtteranceTrigger, State.Cancelling, ShouldCancelOnEarlyBargeInGuard)
            .Permit(Trigger.OutputRequestPublished, State.WaitingForAudio) // Transition when request sent, wait for audio start confirmation
            .Permit(Trigger.OutputRequestFailed, State.Idle)               // LLM/Request failed
            .Permit(Trigger.DetectBargeIn, State.Cancelling)               // Barge-in detected (might happen if TTS is very fast)
            .Permit(_requestCancellationTrigger.Trigger, State.Cancelling) // External cancellation
            .OnExitAsync(EnsureProcessingTaskIsCancelledAsync)             // Cancel LLM task if exiting prematurely
            .Ignore(_audioPlaybackStartedTrigger.Trigger)                  // Wait for WaitingForAudio state
            .Ignore(_audioPlaybackStoppedTrigger.Trigger)
            .Ignore(Trigger.CancellationComplete);

        _stateMachine.Configure(State.WaitingForAudio)
            .PermitIf(_audioPlaybackStartedTrigger, State.Speaking, IsExpectedAudioEventGuard) // Transition to Speaking ONLY when audio starts
            .PermitIf(_audioPlaybackStoppedTrigger, State.Idle, param => IsExpectedAudioEventGuard(param) && (param.StopEvent?.Reason == AssistantStopReason.Error)) // Audio failed *before* starting
            .PermitIf(_audioPlaybackStoppedTrigger, State.Cancelling, param => IsExpectedAudioEventGuard(param) && (param.StopEvent?.Reason == AssistantStopReason.Cancelled)) // Cancelled *before* starting
            .Permit(Trigger.DetectBargeIn, State.Cancelling)                // Barge-in detected while waiting for audio
            .Permit(_requestCancellationTrigger.Trigger, State.Cancelling)  // External cancellation while waiting
            .PermitIf(_receiveUtteranceTrigger, State.Cancelling, ShouldCancelOnEarlyBargeInGuard) // User speaks again while waiting (treat as barge-in)
            .OnExitAsync(EnsureProcessingTaskIsCancelledAsync)              // Ensure processing task is cancelled if leaving prematurely
            .Ignore(Trigger.OutputRequestPublished)                         // Already published
            .Ignore(Trigger.OutputRequestFailed)                            // Should have happened in Processing state
            .Ignore(Trigger.CancellationComplete);

        _stateMachine.Configure(State.Speaking)
            .OnEntryFrom(_audioPlaybackStartedTrigger, param => _logger.LogInformation("FSM: Entered Speaking state for RequestId {RequestId}", param.StartEvent.RequestId))
            .PermitIf(_audioPlaybackStoppedTrigger, State.Idle, param => IsExpectedAudioEventGuard(param) && param.StopEvent?.Reason == AssistantStopReason.CompletedNaturally)
            .PermitIf(_audioPlaybackStoppedTrigger, State.Idle, param => IsExpectedAudioEventGuard(param) && param.StopEvent?.Reason == AssistantStopReason.Error) // Go Idle on error during speech
            .PermitIf(_audioPlaybackStoppedTrigger, State.Cancelling, param => IsExpectedAudioEventGuard(param) && param.StopEvent?.Reason == AssistantStopReason.Cancelled) // If cancelled externally during speech
            .Permit(Trigger.DetectBargeIn, State.Cancelling)                // ** Mid-Speech Barge-in Handling **
            .Permit(_requestCancellationTrigger.Trigger, State.Cancelling)  // External cancellation during speech
            .OnExitAsync(EnsureAudioIsStoppedAsync)                         // Ensure audio stop is requested if exiting for cancellation
            .Ignore(_receiveUtteranceTrigger.Trigger)                       // Ignore new utterances while speaking (handled by barge-in)
            .Ignore(Trigger.OutputRequestPublished)
            .Ignore(Trigger.OutputRequestFailed)
            .Ignore(_audioPlaybackStartedTrigger.Trigger)                   // Already started
            .Ignore(Trigger.CancellationComplete);

        _stateMachine.Configure(State.Cancelling)
            // ** Pass the utterance parameter to StartCancellationFlowAsync **
            .OnEntryFromAsync(Trigger.DetectBargeIn, transition => StartCancellationFlowAsync("Barge-in detected", transition.Source, null)) // No utterance data here
            .OnEntryFromAsync(_requestCancellationTrigger, (param, transition) => StartCancellationFlowAsync(param.Reason, transition.Source, null)) // No utterance data here
            .OnEntryFromAsync(_receiveUtteranceTrigger, (param, transition) => StartCancellationFlowAsync("Early barge-in (new utterance received)", transition.Source, param.Utterance)) // Pass the new utterance
            .OnEntryFromAsync(_audioPlaybackStoppedTrigger, (param, transition) => StartCancellationFlowAsync($"Audio stopped unexpectedly ({param.StopEvent?.Reason})", transition.Source, null)) // No utterance data here
            // ** Cancellation always leads to Idle. Reprocessing is handled by Idle.OnEntryAsync **
            .Permit(Trigger.CancellationComplete, State.Idle)
            .Ignore(_receiveUtteranceTrigger.Trigger) // Ignore external utterances while cancelling
            .Ignore(Trigger.DetectBargeIn)
            .Ignore(Trigger.RequestCancellation)
            .Ignore(Trigger.OutputRequestPublished)
            .Ignore(Trigger.OutputRequestFailed)
            .Ignore(_audioPlaybackStartedTrigger.Trigger)
            .Ignore(_audioPlaybackStoppedTrigger.Trigger);

        _stateMachine.OnTransitioned(t => _logger.LogInformation("FSM Transition: {Source} -> {Destination} via {Trigger}", t.Source, t.Destination, t.Trigger));
        _stateMachine.OnUnhandledTrigger((s, t) => _logger.LogWarning("FSM Unhandled Trigger: Trigger '{Trigger}' is invalid in state '{State}'", t, s));

        // --- End State Machine Configuration ---
    }

    // --- Service Lifecycle Methods --- (Remain the same)
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Conversation Orchestrator (FSM)...");
        _orchestratorLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Start background tasks to process channel events
        _ = ProcessChannelEventsAsync<UserUtteranceCompleted>(_utteranceReader, OnUserUtteranceCompletedInternalAsync, _orchestratorLoopCts.Token);
        _ = ProcessChannelEventsAsync<BargeInDetected>(_bargeInReader, OnBargeInDetectedInternalAsync, _orchestratorLoopCts.Token);
        _ = ProcessChannelEventsAsync<object>(_systemStateReader, OnSystemStateEventInternalAsync, _orchestratorLoopCts.Token); // Add system state listener
        _logger.LogInformation("Conversation Orchestrator (FSM) started.");

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping Conversation Orchestrator (FSM)...");

        // Cancel the main event loop
        if (_orchestratorLoopCts != null && !_orchestratorLoopCts.IsCancellationRequested)
        {
            _logger.LogDebug("Cancelling orchestrator event loop CTS.");
            await CancelCtsAsync(_orchestratorLoopCts);
        }

        // Attempt to cancel any ongoing activity via the state machine
        await RequestCancellationAsync("Orchestrator stopping");

        // Wait a short moment for cancellation to propagate
        await Task.Delay(200);

        _logger.LogInformation("Conversation Orchestrator (FSM) stopped.");
    }

     public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing Conversation Orchestrator (FSM)...");
        await StopAsync(); // Ensure stopped

        // Dispose CTS resources
        lock (_processingLock)
        {
            _activeProcessingCts?.Dispose();
            _activeProcessingCts = null;
        }

        _orchestratorLoopCts?.Dispose();
        _orchestratorLoopCts = null;

        _logger.LogInformation("Conversation Orchestrator (FSM) disposed.");
    }

    // --- FSM Guards --- (Remain the same)
    private bool ShouldProcessUtteranceGuard(UtteranceTriggerParameter param)
    {
        // Basic null checks
        if (_turnTakingStrategy == null || param?.Utterance == null || _stateMachine == null)
        {
            _logger.LogError("Null dependency or parameter in ShouldProcessUtteranceGuard.");
            return false;
        }

        // Use turn-taking strategy based on CURRENT FSM state
        var action = _turnTakingStrategy.DecideAction(param.Utterance, _stateMachine.State);
        _logger.LogDebug("Turn-taking strategy decided action: {Action} for utterance from {User} in state {State}",
                         action, param.Utterance.User, _stateMachine.State);

        var shouldProcess = action == TurnTakingAction.ProcessNow && !string.IsNullOrWhiteSpace(param.Utterance.AggregatedText);

        if (!shouldProcess)
        {
            if (action == TurnTakingAction.ProcessNow)
            {
                _logger.LogWarning("Ignoring empty utterance from {User} although strategy allowed processing.", param.Utterance.User);
            }
            else
            {
                _logger.LogInformation("Strategy requested {Action} for utterance from {User} (State: {State}). Ignoring.", action, param.Utterance.User, _stateMachine.State);
            }
        }

        return shouldProcess;
    }
    private bool IsExpectedAudioEventGuard(SystemEventTriggerParameter param)
    {
        var eventRequestId = param?.StartEvent?.RequestId ?? param?.StopEvent?.RequestId ?? Guid.Empty;
        lock (_processingLock)
        {
            if (eventRequestId != Guid.Empty && eventRequestId == _currentRequestId)
            {
                return true;
            }

            // Log as trace, could be noisy if previous requests' events arrive late
            _logger.LogTrace("Received audio event (Start/Stop) with RequestId {EventRequestId} which does not match current RequestId {CurrentRequestId}. Ignoring.", eventRequestId, _currentRequestId);
            return false;
        }
    }
    private bool ShouldCancelOnEarlyBargeInGuard(UtteranceTriggerParameter param)
    {
        // Basic check: If we receive a non-empty utterance while processing or waiting for audio, cancel.
        var shouldCancel = param?.Utterance != null && !string.IsNullOrWhiteSpace(param.Utterance.AggregatedText);
        if (shouldCancel)
        {
            _logger.LogInformation("Early barge-in detected: Received new utterance from {User} while in {State} state. Will cancel current processing.", param.Utterance.User, _stateMachine.State);
        }
        return shouldCancel;
    }

    // --- FSM Actions ---

    // ** NEW Action: Called when entering Idle state **
    private async Task ProcessPendingUtteranceAsync()
    {
        UserUtteranceCompleted? utteranceToProcess = null;

        // Check if there's an utterance pending reprocessing
        lock (_processingLock) // Use the same lock for consistency, though contention is low here
        {
            if (_utteranceToReprocess != null)
            {
                utteranceToProcess = _utteranceToReprocess;
                _utteranceToReprocess = null; // Consume the pending utterance
                _logger.LogInformation("Idle.OnEntry: Found pending utterance from {User} to reprocess.", utteranceToProcess.User);
            }
        }

        if (utteranceToProcess != null)
        {
            // Fire the trigger to start processing this utterance
            // This will check the ShouldProcessUtteranceGuard again
            _logger.LogDebug("Idle.OnEntry: Firing ReceiveUtterance trigger for pending utterance.");
            await _stateMachine.FireAsync(_receiveUtteranceTrigger, new UtteranceTriggerParameter(utteranceToProcess));
        }
    }


    private async Task StartProcessingFlowAsync(UtteranceTriggerParameter param, StateMachine<State, Trigger>.Transition transition)
    {
        if (param?.Utterance == null)
        {
            _logger.LogError("UtteranceTriggerParameter or Utterance is null in StartProcessingFlowAsync.");
            await _stateMachine.FireAsync(Trigger.OutputRequestFailed); // Fail fast
            return;
        }

        var utterance = param.Utterance;
        var newRequestId = Guid.NewGuid(); // Generate unique ID for this request
        _logger.LogInformation("FSM: Entering Processing state for utterance from {User} (SourceId: {SourceId}, RequestId: {RequestId}).",
                               utterance.User, utterance.SourceId, newRequestId);

        CancellationTokenSource processingCts;
        Task processingTask;

        lock (_processingLock)
        {
            // Clean up previous CTS if any (should be handled by state transitions, but belt-and-suspenders)
            _activeProcessingCts?.Cancel();
            _activeProcessingCts?.Dispose();

            _activeProcessingCts = new CancellationTokenSource();
            processingCts = _activeProcessingCts; // Local variable for the task lambda
            _currentProcessingUtterance = utterance;
            _currentRequestId = newRequestId; // Store current request ID

            // Clear any potentially stale reprocessing request
            _utteranceToReprocess = null;

            _activeProcessingTask = Task.Run(async () =>
            {
                var success = false;
                var taskId = Task.CurrentId ?? -1;
                try
                {
                    _logger.LogDebug("Starting LLM processing task (Task ID: {TaskId}, RequestId: {RequestId})...", taskId, newRequestId);

                    // Execute the workflow, passing the specific RequestId and CTS token
                    success = await ExecuteLlmProcessingWorkflowAsync(utterance, newRequestId, processingCts.Token);

                    if (!processingCts.Token.IsCancellationRequested)
                    {
                        // ** Fire OutputRequestFailed if workflow returned false (e.g., empty LLM response) **
                        var resultTrigger = success ? Trigger.OutputRequestPublished : Trigger.OutputRequestFailed;
                        _logger.LogDebug("LLM processing task (Task ID: {TaskId}, RequestId: {RequestId}) finished. Firing {Trigger}.", taskId, newRequestId, resultTrigger);
                        // Fire internal trigger - FSM handles transition (Processing -> WaitingForAudio or Processing -> Idle)
                        await _stateMachine.FireAsync(resultTrigger);
                    }
                    else
                    {
                        _logger.LogInformation("LLM processing task (Task ID: {TaskId}, RequestId: {RequestId}) was cancelled during execution.", taskId, newRequestId);
                        // Don't fire completion trigger if cancelled externally
                    }
                }
                catch (OperationCanceledException) when (processingCts.IsCancellationRequested)
                {
                    _logger.LogInformation("LLM processing task (Task ID: {TaskId}, RequestId: {RequestId}) cancelled.", taskId, newRequestId);
                    // Don't fire completion trigger
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during LLM processing task (Task ID: {TaskId}, RequestId: {RequestId}).", taskId, newRequestId);
                    success = false;
                    // Fire failure trigger if not cancelled
                    if (!processingCts.IsCancellationRequested)
                    {
                        await _stateMachine.FireAsync(Trigger.OutputRequestFailed);
                    }
                }
                finally
                {
                    _logger.LogDebug("LLM processing task finally block entered (Task ID: {TaskId}, RequestId: {RequestId})", taskId, newRequestId);
                    // Clean up references ONLY IF this is still the active task/request
                    lock (_processingLock)
                    {
                        if (_currentRequestId == newRequestId && _activeProcessingTask?.Id == taskId)
                        {
                            // Only clear if no new request has started and this task is the one finishing
                            _activeProcessingTask = null;
                            // Don't clear CTS/RequestId here, might be needed for cancellation flow or audio correlation
                            _logger.LogDebug("Cleared active task reference for Task ID: {TaskId}, RequestId: {RequestId}", taskId, newRequestId);
                        }
                        else
                        {
                            _logger.LogWarning("Task {TaskId} (RequestId: {RequestId}) finished, but a different task/request {CurrentRequestId} is active or task reference mismatch. Not clearing references.", taskId, newRequestId, _currentRequestId);
                        }
                    }
                }
            }, processingCts.Token); // Pass token to Task.Run

            processingTask = _activeProcessingTask; // Assign to outer variable
        }

        _logger.LogDebug("Created LLM processing task (Task ID: {TaskId}).", processingTask?.Id ?? -1);
        // No await here, task runs in background
        await Task.CompletedTask;
    }

    // ExecuteLlmProcessingWorkflowAsync remains the same
    private async Task<bool> ExecuteLlmProcessingWorkflowAsync(UserUtteranceCompleted utterance, Guid requestId, CancellationToken cancellationToken)
    {
        if (utterance == null)
        {
            throw new ArgumentNullException(nameof(utterance), "Utterance cannot be null in ExecuteLlmProcessingWorkflowAsync");
        }

        string? aggregatedResponse = null;

        try
        {
            // 1. Get Context Snapshot
            _logger.LogDebug("Getting context snapshot (RequestId: {RequestId})...", requestId);
            var contextSnapshot = await _contextManager.GetContextSnapshotAsync(utterance.SourceId);
            cancellationToken.ThrowIfCancellationRequested();

            // 2. Get LLM Response Stream
            _logger.LogDebug("Requesting LLM stream (RequestId: {RequestId})...", requestId);
            var currentMessage = new ChatMessage(utterance.User, utterance.AggregatedText);
            var llmStream = _llmProcessor.GetStreamingChatResponseAsync(currentMessage, contextSnapshot, cancellationToken);

            if (llmStream == null)
            {
                _logger.LogWarning("LLM Processor returned null stream (RequestId: {RequestId}).", requestId);
                return false; // Indicate failure
            }

            // 3. Aggregate LLM Response
            _logger.LogDebug("Aggregating LLM response stream (RequestId: {RequestId})...", requestId);
            var responseBuilder = new StringBuilder();
            await foreach (var chunk in llmStream.WithCancellation(cancellationToken))
            {
                responseBuilder.Append(chunk);
            }
            cancellationToken.ThrowIfCancellationRequested(); // Check after loop

            aggregatedResponse = responseBuilder.ToString();
            _logger.LogInformation("LLM response aggregated (RequestId: {RequestId}, Length: {Length}).", requestId, aggregatedResponse.Length);

            if (string.IsNullOrWhiteSpace(aggregatedResponse))
            {
                _logger.LogWarning("LLM returned empty or whitespace response (RequestId: {RequestId}). Completing without output.", requestId);
                return false; // Indicate no valid output to publish -> leads to OutputRequestFailed trigger
            }

            // 4. Format Response
            string formattedResponse;
            try
            {
                _logger.LogDebug("Applying output formatting strategy (RequestId: {RequestId})...", requestId);
                // Pass metadata including RequestId and utterance end time if needed by formatter/adapter
                var requestMetadata = new Dictionary<string, object> { { "RequestId", requestId }, { "UtteranceEndTime", utterance.EndTimestamp } };
                formattedResponse = _outputFormatter.Format(aggregatedResponse, requestMetadata);
                _logger.LogDebug("Output formatting applied (RequestId: {RequestId}).", requestId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying output formatting strategy (RequestId: {RequestId}). Using raw response.", requestId);
                formattedResponse = aggregatedResponse; // Fallback to raw
            }

            // 5. Publish Output Request
            _logger.LogDebug("Publishing ProcessOutputRequest (RequestId: {RequestId})...", requestId);
            var outputRequest = new ProcessOutputRequest(
                requestId, // Include RequestId
                formattedResponse,
                utterance.SourceId,
                null, // TargetChannelId
                "Default", // OutputTypeHint (could be dynamic)
                new Dictionary<string, object> { { "UtteranceEndTime", utterance.EndTimestamp } } // Pass metadata
            );

            cancellationToken.ThrowIfCancellationRequested();
            await _outputRequestWriter.WriteAsync(outputRequest, cancellationToken);
            _logger.LogDebug("ProcessOutputRequest published (RequestId: {RequestId}).", requestId);

            // 6. Add Assistant Interaction to Context (Do this *after* successfully publishing)
            var assistantInteraction = new Interaction(
                "Assistant", // SourceId for assistant
                ChatMessageRole.Assistant,
                aggregatedResponse, // Store the raw response
                DateTimeOffset.Now
            );
            await _contextManager.AddInteractionAsync(assistantInteraction); // Add context async

            return true; // Indicate success
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("LLM processing workflow cancelled (RequestId: {RequestId}).", requestId);
            return false; // Indicate failure due to cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during LLM processing workflow (RequestId: {RequestId}).", requestId);
            return false; // Indicate failure
        }
    }

    // EnsureProcessingTaskIsCancelledAsync remains the same
    private async Task EnsureProcessingTaskIsCancelledAsync(StateMachine<State, Trigger>.Transition transition)
    {
        // Only cancel if leaving for a reason other than normal completion/failure triggers
        // For Processing: OutputRequestPublished, OutputRequestFailed
        // For WaitingForAudio: AudioPlaybackStarted, AudioPlaybackStopped (Error/Cancelled reason)
        bool shouldCancel = transition.Source switch
        {
            State.Processing => transition.Trigger != Trigger.OutputRequestPublished && transition.Trigger != Trigger.OutputRequestFailed,
            State.WaitingForAudio => transition.Trigger != Trigger.AudioPlaybackStarted && transition.Trigger != Trigger.AudioPlaybackStopped,
            _ => false // Should not happen from other states
        };

        if (shouldCancel)
        {
            _logger.LogDebug("FSM: Exiting {SourceState} state via trigger {Trigger}. Ensuring active LLM task is cancelled.", transition.Source, transition.Trigger);
            await CancelActiveProcessingTaskInternalAsync($"Exiting {transition.Source} state due to {transition.Trigger}");
        }
        await Task.CompletedTask; // Keep async signature
    }

    // EnsureAudioIsStoppedAsync remains the same
    private async Task EnsureAudioIsStoppedAsync(StateMachine<State, Trigger>.Transition transition)
    {
        // Only stop audio if leaving Speaking for a reason other than AudioPlaybackStopped
        if (transition.Trigger != Trigger.AudioPlaybackStopped)
        {
            _logger.LogDebug("FSM: Exiting Speaking state via trigger {Trigger}. Ensuring audio playback is stopped.", transition.Trigger);
            await StopAudioPlaybackInternalAsync($"Exiting Speaking state due to {transition.Trigger}");
        }
        await Task.CompletedTask; // Keep async signature
    }


    // ** MODIFIED Action: Executed when entering the Cancelling state **
    // Now accepts the interrupting utterance (if any)
    private async Task StartCancellationFlowAsync(string reason, State sourceState, UserUtteranceCompleted? interruptingUtterance)
    {
        _logger.LogInformation("FSM: Entering Cancelling state from {SourceState}. Reason: {Reason}. Interrupting Utterance: {HasInterrupting}",
            sourceState, reason, interruptingUtterance != null);

        Task? taskToAwait = null;
        int? taskToAwaitId = null;
        var cancelAudio = false;
        var cancelProcessing = false;
        var currentRequestIdSnapshot = Guid.Empty;

        lock (_processingLock)
        {
            currentRequestIdSnapshot = _currentRequestId; // Capture the ID of the request being cancelled
            taskToAwait = _activeProcessingTask; // Get reference to potentially await
            taskToAwaitId = taskToAwait?.Id;
            cancelAudio = sourceState == State.Speaking; // Stop audio only if we were actually speaking
            // Cancel processing task CTS if we were processing OR waiting for audio OR speaking
            cancelProcessing = sourceState == State.Processing || sourceState == State.WaitingForAudio || sourceState == State.Speaking;

            // ** Store the interrupting utterance for potential reprocessing **
            if (interruptingUtterance != null)
            {
                _utteranceToReprocess = interruptingUtterance;
                _logger.LogDebug("Stored interrupting utterance from {User} for potential reprocessing.", interruptingUtterance.User);
            }
            else
            {
                _utteranceToReprocess = null; // Ensure it's clear if no new utterance caused cancellation
            }
        }

        // 1. Signal cancellation/stop
        if (cancelProcessing)
        {
            await CancelActiveProcessingTaskInternalAsync(reason);
        }

        if (cancelAudio)
        {
            // This will trigger AssistantSpeakingStopped(Cancelled) event eventually
            await StopAudioPlaybackInternalAsync(reason);
        }

        // 2. Wait briefly for actions to take effect
        // A more robust implementation might wait for task completion or specific events
        _logger.LogDebug("Waiting briefly for cancellation actions to take effect (RequestId: {RequestId})...", currentRequestIdSnapshot);
        await Task.Delay(TimeSpan.FromMilliseconds(100)); // Reduced delay

        // 3. Clean up state associated with the *cancelled* request
        _logger.LogDebug("Cleaning up resources for cancelled RequestId {RequestId}.", currentRequestIdSnapshot);
        lock (_processingLock)
        {
            // Clean up resources associated with the cancelled request *only if it's still the current one*
            if (_currentRequestId == currentRequestIdSnapshot)
            {
                _activeProcessingTask = null; // Clear task ref
                _activeProcessingCts?.Dispose();
                _activeProcessingCts = null;
                _currentProcessingUtterance = null;
                _currentRequestId = Guid.Empty;
                _logger.LogDebug("Cleaned up resources after cancellation for RequestId {RequestId}.", currentRequestIdSnapshot);
            }
            else
            {
                _logger.LogWarning("Cancellation flow finished for RequestId {RequestId}, but a newer request {NewRequestId} is now active. Not clearing resources.", currentRequestIdSnapshot, _currentRequestId);
            }
        }

        // 4. Complete the cancellation - always transition to Idle
        // The Idle state's OnEntryAsync handler will check _utteranceToReprocess
        _logger.LogDebug("Cancellation flow finished for RequestId {RequestId}. Firing CancellationComplete trigger to transition to Idle.", currentRequestIdSnapshot);
        try
        {
            if (_stateMachine != null && _stateMachine.State == State.Cancelling)
            {
                // This trigger always moves from Cancelling to Idle
                await _stateMachine.FireAsync(Trigger.CancellationComplete);
            }
            else
            {
                _logger.LogWarning("State machine was null or not in Cancelling state ({State}) when trying to fire CancellationComplete trigger for RequestId {RequestId}.", _stateMachine?.State, currentRequestIdSnapshot);
            }
        }
        catch (Exception fireEx)
        {
            _logger.LogError(fireEx, "Error firing FSM trigger CancellationComplete for RequestId {RequestId}", currentRequestIdSnapshot);
        }
    }


    // --- Internal Helper Methods --- (Remain the same)
    private async Task CancelActiveProcessingTaskInternalAsync(string reason)
    {
        CancellationTokenSource? ctsToCancel = null;
        var requestId = Guid.Empty;
        int? taskId = null;

        lock (_processingLock)
        {
            // Get the CTS associated with the *current* request ID
            if (_currentRequestId != Guid.Empty)
            {
                ctsToCancel = _activeProcessingCts;
                requestId = _currentRequestId;
                taskId = _activeProcessingTask?.Id;
            }
        }

        if (ctsToCancel != null && !ctsToCancel.IsCancellationRequested)
        {
            _logger.LogInformation("Requesting cancellation of active processing task (Task ID: {TaskId}, RequestId: {RequestId}). Reason: {Reason}", taskId, requestId, reason);
            await CancelCtsAsync(ctsToCancel);
            _logger.LogDebug("Cancellation signal sent to active processing task's CTS (Task ID: {TaskId}, RequestId: {RequestId}).", taskId, requestId);
        }
        else if (ctsToCancel == null && requestId != Guid.Empty)
        {
            _logger.LogWarning("Attempted to cancel active processing task for RequestId {RequestId}, but no active CTS found.", requestId);
        }
        else if (ctsToCancel != null && ctsToCancel.IsCancellationRequested)
        {
            _logger.LogDebug("Attempted to cancel active processing task (Task ID: {TaskId}, RequestId: {RequestId}), but cancellation was already requested.", taskId, requestId);
        }
        else
        {
            _logger.LogDebug("Attempted to cancel active processing task, but no request is currently active.");
        }
    }
    private async Task StopAudioPlaybackInternalAsync(string reason)
    {
        Guid requestIdToStop;
        lock (_processingLock)
        {
            requestIdToStop = _currentRequestId;
        }

        if (requestIdToStop != Guid.Empty)
        {
            _logger.LogInformation("Requesting audio playback stop (RequestId: {RequestId}). Reason: {Reason}", requestIdToStop, reason);
            try
            {
                await _audioOutputService.StopPlaybackAsync();
                // Note: The AssistantSpeakingStopped(Cancelled) event should be published
                // by the AudioOutputService's PlayTextStreamAsync finally block.
                // The orchestrator will react to that event via OnSystemStateEventInternalAsync.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling StopPlaybackAsync for RequestId: {RequestId}", requestIdToStop);
            }
        }
        else
        {
            _logger.LogDebug("Attempted to stop audio playback, but no request is currently active.");
        }
    }
    private async Task CancelCtsAsync(CancellationTokenSource cts)
    {
        if (cts == null || cts.IsCancellationRequested)
        {
            return;
        }

        try
        {
            // Use CancelAsync if available (newer .NET versions), otherwise fallback to Cancel
#if NET6_0_OR_GREATER
            await cts.CancelAsync();
#else
            cts.Cancel();
            await Task.CompletedTask; // Keep async signature
#endif
        }
        catch (ObjectDisposedException)
        {
            _logger.LogWarning("Attempted to cancel an already disposed CTS.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while cancelling CTS.");
        }
    }

    // --- Event Processing Loop --- (Remains the same)
    private async Task ProcessChannelEventsAsync<T>(ChannelReader<T> reader, Func<T, Task> processor, CancellationToken ct) where T : class
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(processor);

        var eventTypeName = typeof(T).Name;
        _logger.LogDebug("Starting event processing loop for type {EventType}.", eventTypeName);

        try
        {
            await foreach (var item in reader.ReadAllAsync(ct))
            {
                if (ct.IsCancellationRequested)
                {
                    break; // Check before processing
                }

                if (item == null)
                {
                    _logger.LogWarning("Read a null item from channel {EventType}.", eventTypeName);
                    continue;
                }

                try
                {
                    _logger.LogTrace("Processing event of type {EventType}.", eventTypeName);
                    await processor(item); // Process the event
                }
                catch (InvalidOperationException ioex) // Catch FSM errors specifically
                {
                    _logger.LogWarning(ioex, "FSM Invalid Operation while processing {EventType}. Current State: {State}. Event likely ignored.", eventTypeName, _stateMachine?.State);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing item of type {EventType}.", eventTypeName);
                    // Decide if processing should continue or loop should break on error
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Event processing loop cancelled for type {EventType}.", eventTypeName);
        }
        catch (ChannelClosedException)
        {
            _logger.LogInformation("Channel closed for type {EventType}.", eventTypeName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in event processing loop for type {EventType}.", eventTypeName);
        }
        finally
        {
            _logger.LogInformation("Exiting event processing loop for type {EventType}.", eventTypeName);
        }
    }


    // --- Internal Event Handlers (Called by ProcessChannelEventsAsync) --- (Remain the same)
    private async Task OnUserUtteranceCompletedInternalAsync(UserUtteranceCompleted utterance)
    {
        if (utterance == null)
        {
            _logger.LogWarning("Received null UserUtteranceCompleted event.");
            return;
        }

        _logger.LogInformation("Orchestrator received utterance from {User} (SourceId: {SourceId}). Text: '{Text}'",
                               utterance.User, utterance.SourceId, utterance.AggregatedText);

        // Add interaction to context regardless of FSM state? Yes.
        await AddUserInteractionToContextAsync(utterance);

        var triggerParam = new UtteranceTriggerParameter(utterance);

        if (_stateMachine == null)
        {
            _logger.LogError("State machine is not initialized.");
            return;
        }

        // Check if the state machine can handle the trigger in the current state
        if (_stateMachine.CanFire(Trigger.ReceiveUtterance))
        {
             _logger.LogDebug("Attempting to fire ReceiveUtterance trigger (Current State: {State})...", _stateMachine.State);
             // FireAsync will handle guards and transitions, including early barge-in
            try
            {
                await _stateMachine.FireAsync(_receiveUtteranceTrigger, triggerParam);
                _logger.LogDebug("ReceiveUtterance trigger processing complete (New State: {State}).", _stateMachine.State);
            }
            catch (InvalidOperationException fsmEx) // Should not happen due to CanFire check, but good practice
            {
                _logger.LogWarning(fsmEx, "Could not fire ReceiveUtterance trigger in state {State} despite CanFire check. Utterance likely ignored.", _stateMachine.State);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error firing ReceiveUtterance trigger.");
            }
        }
        else
        {
             // If the trigger cannot be fired (e.g., already processing/speaking/cancelling), log it.
             // The TurnTakingStrategy might also decide to ignore it based on state.
             _logger.LogInformation("ReceiveUtterance trigger ignored in current state {State} for utterance from {User}.", _stateMachine.State, utterance.User);
             // Optionally, store this utterance if a queuing mechanism is desired later
             // lock(_processingLock) { _utteranceToReprocess = utterance; } // Example: Simple overwrite queuing
        }
    }
    private async Task OnBargeInDetectedInternalAsync(BargeInDetected bargeInEvent)
    {
        if (bargeInEvent == null)
        {
            _logger.LogWarning("Received null BargeInDetected event.");
            return;
        }

        _logger.LogInformation("Orchestrator received barge-in from {SourceId}.", bargeInEvent.SourceId);

        if (_stateMachine == null)
        {
            _logger.LogError("State machine is not initialized.");
            return;
        }

        if (_stateMachine.CanFire(Trigger.DetectBargeIn))
        {
            _logger.LogDebug("Attempting to fire DetectBargeIn trigger (Current State: {State})...", _stateMachine.State);
            // FireAsync will handle transitions (Speaking -> Cancelling, WaitingForAudio -> Cancelling)
            try
            {
                await _stateMachine.FireAsync(Trigger.DetectBargeIn); // No parameters needed for this trigger
                _logger.LogDebug("DetectBargeIn trigger processing complete (New State: {State}).", _stateMachine.State);
            }
            catch (InvalidOperationException fsmEx)
            {
                _logger.LogWarning(fsmEx, "Could not fire DetectBargeIn trigger in state {State} despite CanFire check. Barge-in likely ignored.", _stateMachine.State);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error firing DetectBargeIn trigger.");
            }
        }
         else
        {
             _logger.LogInformation("DetectBargeIn trigger ignored in current state {State}.", _stateMachine.State);
        }
    }
    private async Task OnSystemStateEventInternalAsync(object systemEvent)
    {
        if (systemEvent == null)
        {
            return;
        }

        Guid eventRequestId = Guid.Empty;
        StateMachine<State, Trigger>.TriggerWithParameters<SystemEventTriggerParameter>? trigger = null;
        SystemEventTriggerParameter? param = null;
        Trigger triggerType = default; // For logging

        switch (systemEvent)
        {
            case AssistantSpeakingStarted startEvent:
                _logger.LogDebug("Received AssistantSpeakingStarted (RequestId: {RequestId})", startEvent.RequestId);
                eventRequestId = startEvent.RequestId;
                trigger = _audioPlaybackStartedTrigger;
                param = new SystemEventTriggerParameter(startEvent);
                triggerType = Trigger.AudioPlaybackStarted;
                break;

            case AssistantSpeakingStopped stopEvent:
                _logger.LogDebug("Received AssistantSpeakingStopped (RequestId: {RequestId}, Reason: {Reason})", stopEvent.RequestId, stopEvent.Reason);
                eventRequestId = stopEvent.RequestId;
                trigger = _audioPlaybackStoppedTrigger;
                param = new SystemEventTriggerParameter(stopEvent);
                triggerType = Trigger.AudioPlaybackStopped;
                break;

            default:
                _logger.LogTrace("Ignoring system event of type {Type}", systemEvent.GetType().Name);
                return; // Ignore other system events
        }

        if (_stateMachine == null || trigger == null || param == null)
        {
            _logger.LogError("State machine or trigger/param is null during system event processing.");
            return;
        }

        // Check if the event corresponds to the currently active request
        Guid activeRequestId;
        lock (_processingLock)
        {
            activeRequestId = _currentRequestId;
        }

        if (activeRequestId == Guid.Empty || eventRequestId != activeRequestId)
        {
            _logger.LogTrace("Ignoring audio event ({EventType}) for RequestId {EventRequestId} as it doesn't match active RequestId {CurrentRequestId}.",
                             systemEvent.GetType().Name, eventRequestId, activeRequestId);
            return;
        }

        // Check if the FSM can fire the trigger before attempting
        if (_stateMachine.CanFire(trigger.Trigger))
        {
            _logger.LogDebug("Attempting to fire {Trigger} trigger (Current State: {State}, RequestId: {RequestId})...", triggerType, _stateMachine.State, eventRequestId);
            // Fire the specific trigger - let the FSM handle transitions based on current state and event reason
            try
            {
                await _stateMachine.FireAsync(trigger, param);
                _logger.LogDebug("{Trigger} trigger processing complete (New State: {State}).", triggerType, _stateMachine.State);
            }
            catch (InvalidOperationException fsmEx) // Should not happen due to CanFire check
            {
                _logger.LogWarning(fsmEx, "Could not fire {Trigger} trigger in state {State} despite CanFire check. Event likely ignored.", triggerType, _stateMachine.State);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error firing {Trigger} trigger.", triggerType);
            }
        }
        else
        {
             _logger.LogInformation("{Trigger} trigger ignored in current state {State} for RequestId {RequestId}.", triggerType, _stateMachine.State, eventRequestId);
        }


        // If audio stopped (for any reason) and we are now idle, clear the request ID and related state
        // This cleanup happens *after* the state transition to Idle occurs
        if (systemEvent is AssistantSpeakingStopped && _stateMachine.State == State.Idle)
        {
            lock (_processingLock)
            {
                if (_currentRequestId == eventRequestId) // Double check it hasn't changed
                {
                    _logger.LogDebug("Clearing RequestId {RequestId} and related state as audio stopped and FSM is Idle.", eventRequestId);
                    _currentRequestId = Guid.Empty;
                    _currentProcessingUtterance = null; // Clear utterance too
                    // Dispose CTS if not already disposed
                    _activeProcessingCts?.Dispose();
                    _activeProcessingCts = null;
                    // Task reference should have been cleared by the task's finally block or cancellation flow
                    if(_activeProcessingTask != null)
                    {
                         _logger.LogWarning("Active processing task reference was not cleared before returning to Idle state for RequestId {RequestId}. Task Status: {Status}", eventRequestId, _activeProcessingTask.Status);
                         _activeProcessingTask = null; // Clear it defensively
                    }
                }
            }
        }
    }
    private async Task AddUserInteractionToContextAsync(UserUtteranceCompleted utterance)
    {
        if (utterance == null || _contextManager == null)
        {
            return;
        }

        var userInteraction = new Interaction(
            utterance.SourceId, // Use SourceId from utterance
            ChatMessageRole.User,
            utterance.AggregatedText,
            utterance.EndTimestamp // Use utterance end time
        );

        try
        {
            await _contextManager.AddInteractionAsync(userInteraction);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add user interaction to context history.");
        }
    }
    private async Task RequestCancellationAsync(string reason)
    {
        if (_stateMachine == null)
        {
            return;
        }

        var currentState = _stateMachine.State; // Read state

        if (currentState == State.Processing || currentState == State.WaitingForAudio || currentState == State.Speaking)
        {
            var cancelParam = new CancellationTriggerParameter(reason, currentState);
            if (_stateMachine.CanFire(Trigger.RequestCancellation))
            {
                _logger.LogDebug("Firing RequestCancellation trigger (Source State: {State}). Reason: {Reason}", currentState, reason);
                try
                {
                    await _stateMachine.FireAsync(_requestCancellationTrigger, cancelParam);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error firing RequestCancellation trigger. Attempting direct cancel.");
                    // Attempt direct cancellation as fallback
                    await CancelActiveProcessingTaskInternalAsync($"{reason} (FSM fire failed)");
                    await StopAudioPlaybackInternalAsync($"{reason} (FSM fire failed)");
                }
            }
            else
            {
                _logger.LogWarning("Cannot fire RequestCancellation trigger in state {State}. Attempting direct cancel.", currentState);
                // Attempt direct cancellation as fallback
                await CancelActiveProcessingTaskInternalAsync($"{reason} (FSM cannot fire)");
                await StopAudioPlaybackInternalAsync($"{reason} (FSM cannot fire)");
            }
        }
        else
        {
            _logger.LogDebug("RequestCancellation ignored as system is in state {State}.", currentState);
        }
    }


    // --- Trigger Definitions --- (Remain the same)
    private enum Trigger
    {
        ReceiveUtterance,       // User utterance completed
        DetectBargeIn,          // User started speaking while assistant was speaking/processing (meeting criteria)
        RequestCancellation,    // Explicit external request to stop current activity

        // Internal Triggers
        OutputRequestPublished, // LLM processing finished, request sent to adapters
        OutputRequestFailed,    // LLM processing failed before request could be sent
        AudioPlaybackStarted,   // AssistantSpeakingStarted received for the current request
        AudioPlaybackStopped,   // AssistantSpeakingStopped received for the current request
        CancellationComplete    // Cancellation process finished
    }

    // --- Trigger Parameter Classes --- (Remain the same)
    [DebuggerNonUserCode] // Optional: Hide from debugger call stack
    private class UtteranceTriggerParameter(UserUtteranceCompleted utterance)
    {
        public UserUtteranceCompleted Utterance { get; } = utterance ?? throw new ArgumentNullException(nameof(utterance));
    }

    [DebuggerNonUserCode]
    private class CancellationTriggerParameter(string reason, State sourceState)
    {
        public string Reason { get; } = reason;
        public State Source { get; } = sourceState; // Track which state triggered cancellation
    }

    [DebuggerNonUserCode]
    private class SystemEventTriggerParameter
    {
        // Private constructor ensures one of the events is set
        private SystemEventTriggerParameter(AssistantSpeakingStarted? startEvent, AssistantSpeakingStopped? stopEvent)
        {
            StartEvent = startEvent;
            StopEvent = stopEvent;
        }

        public SystemEventTriggerParameter(AssistantSpeakingStarted startEvent)
            : this(startEvent ?? throw new ArgumentNullException(nameof(startEvent)), null) { }

        public SystemEventTriggerParameter(AssistantSpeakingStopped stopEvent)
            : this(null, stopEvent ?? throw new ArgumentNullException(nameof(stopEvent))) { }

        public AssistantSpeakingStarted? StartEvent { get; }
        public AssistantSpeakingStopped? StopEvent { get; }
    }
}
