using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Audio.Player;
using PersonaEngine.Lib.Core.Conversation.Common.Messaging;
using PersonaEngine.Lib.Core.Conversation.Contracts.Events;
using PersonaEngine.Lib.LLM;
using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.UI.Common;
using PersonaEngine.Lib.Vision;

using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.Core.Conversation;

public sealed class ConversationManagerRefactor : IAsyncDisposable, IStartupTask
{
    // Context, LLM, Logger, MainCts, Topics remain
    private readonly string                  _context = "Relaxed discussion in discord voice chat.";
    private readonly IChatEngine             _llmEngine;
    private readonly ILogger                 _logger;
    private readonly CancellationTokenSource _mainCts = new();
    private readonly List<string>            _topics  = ["casual conversation"];

    // --- REMOVED Potential Barge-in Text ---
    // private readonly StringBuilder _potentialBargeInText = new();
    // --- END REMOVED ---

    // Task to process incoming events (Utterances and Barge-in)
    private readonly Task          _eventProcessingTask;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    // --- MODIFIED Channel Readers / Added Writers ---
    // --- REMOVED _transcriptionEventReader --- (No longer listening for PotentialTranscriptUpdate)
    // private readonly ChannelReader<ITranscriptionEvent> _transcriptionEventReader;
    private readonly ChannelReader<UserUtteranceCompleted> _utteranceCompletionReader;
    private readonly ChannelReader<BargeInDetected>        _bargeInReader;
    private readonly ChannelWriter<object>                 _systemStateWriter;
    // --- END MODIFIED ---

    // TTS, VisualQA remain
    private readonly ITtsEngine       _ttsSynthesizer;
    private readonly IVisualQAService _visualQaService;

    // State variables
    private CancellationTokenSource     _activeLlmCts = new();
    private Task?                       _activeProcessingTask;
    private string                      _currentSpeaker = "User";
    private TaskCompletionSource<bool>? _firstTokenTcs;
    private bool                        _isInStreamingState = false; // Still useful for internal logic, but also published now
    // --- REMOVED Potential Barge-in StartTime ---
    // private DateTimeOffset? _potentialBargeInStartTime;
    // --- END REMOVED ---
    private ConversationState _state = ConversationState.Idle;
    
    public ConversationManagerRefactor(
        IChatEngine                     llmEngine,
        ITtsEngine                      ttsSynthesizer,
        IVisualQAService                visualQaService,
        IAggregatedStreamingAudioPlayer audioPlayer,
        IChannelRegistry                channelRegistry,
        ILogger<ConversationManager>    logger)
    {
        _llmEngine       = llmEngine ?? throw new ArgumentNullException(nameof(llmEngine));
        _ttsSynthesizer  = ttsSynthesizer ?? throw new ArgumentNullException(nameof(ttsSynthesizer));
        _visualQaService = visualQaService ?? throw new ArgumentNullException(nameof(visualQaService));
        AudioPlayer      = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
        _logger          = logger ?? throw new ArgumentNullException(nameof(logger));

        // --- Get Readers/Writers from Registry ---
        _utteranceCompletionReader = channelRegistry?.UtteranceCompletionEvents.Reader ?? throw new ArgumentNullException(nameof(channelRegistry));
        _bargeInReader             = channelRegistry?.BargeInEvents.Reader ?? throw new ArgumentNullException(nameof(channelRegistry));     // Get BargeIn reader
        _systemStateWriter         = channelRegistry?.SystemStateEvents.Writer ?? throw new ArgumentNullException(nameof(channelRegistry)); // Get SystemState writer
        // --- END Get Readers/Writers ---
        
        _visualQaService.StartAsync().ConfigureAwait(false);

        // --- Start single task to process BOTH event types ---
        _eventProcessingTask = ProcessIncomingEventsAsync(_mainCts.Token);
        // --- END Start Task ---

        _logger.LogInformation("ConversationManager initialized and ready");
    }
    
    public IAggregatedStreamingAudioPlayer AudioPlayer { get; }
    
    public async ValueTask DisposeAsync()
    {
        // Dispose logic remains similar
        _logger.LogInformation("Disposing ConversationManager...");
        try
        {
            if (!_mainCts.IsCancellationRequested) await _mainCts.CancelAsync();
            await CancelLlmProcessingAsync(true); // Ensure active processing is cancelled
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try {
                await Task.WhenAll(_eventProcessingTask).WaitAsync(timeoutCts.Token);
            } catch (OperationCanceledException) { _logger.LogWarning("Timeout waiting for task during disposal."); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Error during task wait in disposal."); }
        } catch (OperationCanceledException) { /* Expected */ }
        catch (Exception ex) { _logger.LogError(ex, "Error during ConversationManager disposal"); }
        finally
        {
            _stateLock.Dispose();
            _activeLlmCts.Dispose();
            _mainCts.Dispose();
            // Signal completion of writing system state events? Maybe not needed if registry is singleton.
            // _systemStateWriter.TryComplete();
        }
        _logger.LogInformation("ConversationManager disposed successfully");
    }

    public void Execute(GL gl) { }

    private async Task ProcessIncomingEventsAsync(CancellationToken cancellationToken)
    {
         _logger.LogInformation("Starting incoming event processing loop (Utterances & BargeIn)");

        // Combine reading from Utterance and BargeIn channels
        var utteranceTask = ReadChannelAsync(_utteranceCompletionReader, cancellationToken);
        var bargeInTask = ReadChannelAsync(_bargeInReader, cancellationToken);

        var tasks = new List<Task> { utteranceTask, bargeInTask };

        try
        {
            while (!cancellationToken.IsCancellationRequested && tasks.Count > 0)
            {
                var completedTask = await Task.WhenAny(tasks);

                if (completedTask == utteranceTask)
                {
                     var (utterance, completed) = await utteranceTask;
                     if (utterance != null) await HandleCompletedUtteranceAsync(utterance, cancellationToken);

                     if (completed) {
                         _logger.LogInformation("UtteranceCompletionEvents channel completed.");
                         tasks.Remove(utteranceTask); utteranceTask = null;
                     } else {
                         if(utteranceTask != null) tasks.Remove(utteranceTask);
                         utteranceTask = ReadChannelAsync(_utteranceCompletionReader, cancellationToken);
                         tasks.Add(utteranceTask);
                     }
                }
                else if (completedTask == bargeInTask)
                {
                    var (bargeIn, completed) = await bargeInTask;
                    if (bargeIn != null) await HandleBargeInDetectedAsync(bargeIn, cancellationToken); // New handler

                     if (completed) {
                         _logger.LogInformation("BargeInEvents channel completed.");
                         tasks.Remove(bargeInTask); bargeInTask = null;
                     } else {
                         if(bargeInTask != null) tasks.Remove(bargeInTask);
                         bargeInTask = ReadChannelAsync(_bargeInReader, cancellationToken);
                         tasks.Add(bargeInTask);
                     }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { _logger.LogInformation("Event processing loop cancelled via token."); }
        catch (Exception ex)
        { _logger.LogError(ex, "Error in combined event processing loop."); }
        finally
        { _logger.LogInformation("Combined event processing loop finished."); }
    }
    
    private async Task<(T? item, bool completed)> ReadChannelAsync<T>(ChannelReader<T> reader, CancellationToken ct) where T: class
    {
        try {
            if (await reader.WaitToReadAsync(ct)) {
                if (reader.TryRead(out var item)) {
                    return (item, false);
                }
            }
        } catch (OperationCanceledException) { /* Expected */ }
        catch (ChannelClosedException) { /* Expected */ }
        return (null, true); // Channel completed or cancelled
    }
    
    // --- REMOVED HandlePotentialTranscriptAsync ---
    // private async Task HandlePotentialTranscriptAsync(PotentialTranscriptUpdate potentialSegment, CancellationToken cancellationToken) { ... }
    // --- END REMOVED ---

    // --- NEW Handler for BargeInDetected Event ---
    private async Task HandleBargeInDetectedAsync(BargeInDetected bargeInEvent, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received BargeInDetected event from SourceId: {SourceId}. Text: '{Text}'",
                               bargeInEvent.SourceId, bargeInEvent.DetectedText ?? "N/A");

        // Simple reaction: Trigger the existing barge-in mechanism
        // More sophisticated logic could check SourceId, current state details etc.
        // Check if we are actually in a state where barge-in makes sense (Streaming)
        await _stateLock.WaitAsync(cancellationToken); // Need lock to check state safely
        bool shouldTrigger = _state == ConversationState.Streaming;
        _stateLock.Release(); // Release lock before potentially long-running operation

        if (shouldTrigger)
        {
            _logger.LogDebug("State is Streaming, triggering barge-in response.");
            await TriggerBargeInAsync(cancellationToken); // Call the existing method
        }
        else
        {
            _logger.LogWarning("BargeInDetected event received, but current state is {State}. Ignoring event.", _state);
        }
    }
    // --- END NEW Handler ---
    
    private async Task HandleCompletedUtteranceAsync(UserUtteranceCompleted utterance, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received completed utterance: Source='{SourceId}', User='{User}', Length={Length}",
                               utterance.SourceId, utterance.User, utterance.AggregatedText.Length);

        // --- REMOVED Barge-in state reset here ---
        // _potentialBargeInStartTime = null;
        // _potentialBargeInText.Clear();

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            var currentState = _state;
            _logger.LogDebug("HandleCompletedUtteranceAsync. Current state: {State}", currentState);

            switch ( currentState )
            {
                case ConversationState.Idle:
                    if ( !string.IsNullOrWhiteSpace(utterance.AggregatedText) )
                    {
                        _logger.LogInformation("Idle state: Processing completed utterance.");
                        _currentSpeaker = utterance.User;
                        await TransitionToStateAsync(ConversationState.Processing);
                        _ = StartLlmProcessingTaskAsync(utterance, CancellationToken.None); // Fire and forget
                    }
                    else
                    {
                        _logger.LogWarning("Idle state: Received completed utterance with empty text. Ignoring.");
                    }

                    break;
                case ConversationState.Processing:
                case ConversationState.Streaming:
                case ConversationState.Cancelling:
                    _logger.LogWarning("Received completed utterance while in state {State}. Utterance from '{User}' ignored for now: '{Text}'",
                                       currentState, utterance.User, utterance.AggregatedText.Substring(0, Math.Min(utterance.AggregatedText.Length, 50)));

                    // TODO: Implement queuing or alternative handling
                    break;
            }
        }
        finally
        {
            _stateLock.Release();
        }

        _logger.LogDebug("HandleCompletedUtteranceAsync finished.");
    }

    // --- REMOVED HandleTranscriptionSegmentAsync ---
    // private async Task HandleTranscriptionSegmentAsync(CancellationToken cancellationToken) { ... }
    // --- END REMOVED ---


    private Task StartLlmProcessingTaskAsync(UserUtteranceCompleted utterance, CancellationToken externalCancellationToken)
    {
        return Task.Run(async () => {
                            try {
                                _logger.LogDebug("Task.Run: Executing StartLlmProcessingAsync for utterance from {User}.", utterance.User);
                                await StartLlmProcessingAsync(utterance, externalCancellationToken);
                                _logger.LogDebug("Task.Run: StartLlmProcessingAsync completed.");
                            } catch (Exception ex) {
                                _logger.LogError(ex, "Unhandled exception directly within StartLlmProcessingAsync execution task for utterance from {User}.", utterance.User);
                                await _stateLock.WaitAsync(CancellationToken.None);
                                try {
                                    _logger.LogWarning("Attempting to reset state to Idle due to StartLlmProcessingAsync task failure.");
                                    _activeProcessingTask = null;
                                    await TransitionToStateAsync(ConversationState.Idle);
                                } catch (Exception lockEx) { _logger.LogError(lockEx, "Failed to acquire lock or reset state after StartLlmProcessingAsync task failure."); }
                                finally { if (_stateLock.CurrentCount == 0) _stateLock.Release(); }
                            }
                        });
    }

    private async Task StartLlmProcessingAsync(UserUtteranceCompleted utterance, CancellationToken externalCancellationToken)
    {
        _logger.LogDebug("StartLlmProcessingAsync entered for completed utterance.");
        using var linkedCts     = CancellationTokenSource.CreateLinkedTokenSource(_mainCts.Token, externalCancellationToken);
        var       combinedToken = linkedCts.Token;

        await _stateLock.WaitAsync(combinedToken);
        try
        {
            if ( _state != ConversationState.Processing )
            {
                _logger.LogWarning("StartLlmProcessingAsync: State changed to {State} before processing could start. Aborting.", _state);

                return;
            }

            _logger.LogInformation("StartLlmProcessingAsync: Processing utterance ({Length} chars) from '{User}': '{Text}'", utterance.AggregatedText.Length, utterance.User, utterance.AggregatedText.Substring(0, Math.Min(utterance.AggregatedText.Length, 100)));
            _activeLlmCts?.Dispose();
            _activeLlmCts = CancellationTokenSource.CreateLinkedTokenSource(combinedToken);
        }
        finally
        {
            _stateLock.Release();
        }

        var   llmCancellationToken  = _activeLlmCts.Token;
        Task? currentProcessingTask = null;
        try
        {
            _logger.LogDebug("StartLlmProcessingAsync: Calling ProcessLlmRequestAsync.");
            currentProcessingTask = ProcessLlmRequestAsync(utterance, llmCancellationToken);

            await _stateLock.WaitAsync(combinedToken);
            try
            {
                if ( _state == ConversationState.Processing && _activeProcessingTask == null )
                {
                    _activeProcessingTask = currentProcessingTask;
                    _logger.LogDebug("StartLlmProcessingAsync: ProcessLlmRequestAsync called, task assigned (Id: {TaskId}).", currentProcessingTask.Id);
                }
                else
                {
                    _logger.LogWarning("StartLlmProcessingAsync: Could not assign active task. State: {State}, Existing Task: {TaskId}. Attempting cancellation of new task.", _state, _activeProcessingTask?.Id);
                    _activeLlmCts.Cancel();

                    throw new OperationCanceledException("State changed before LLM task could be tracked.");
                }
            }
            finally
            {
                _stateLock.Release();
            }

            _logger.LogDebug("StartLlmProcessingAsync: Setting up continuation task.");
            _ = currentProcessingTask.ContinueWith(task => _ = HandleProcessingTaskCompletion(task), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            _logger.LogDebug("StartLlmProcessingAsync: Continuation task setup complete.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "StartLlmProcessingAsync: Synchronous exception during ProcessLlmRequestAsync call or task assignment/continuation setup.");
            await _stateLock.WaitAsync(CancellationToken.None);
            try
            {
                _logger.LogWarning("StartLlmProcessingAsync: Resetting state to Idle due to synchronous startup failure.");
                if ( _activeProcessingTask == currentProcessingTask )
                {
                    _activeProcessingTask = null;
                }

                if ( _state == ConversationState.Processing || _state == ConversationState.Cancelling )
                {
                    await TransitionToStateAsync(ConversationState.Idle);
                }
            }
            finally
            {
                _stateLock.Release();
            }
        }

        _logger.LogDebug("StartLlmProcessingAsync finished initiation.");
    }

    private async Task HandleProcessingTaskCompletion(Task completedTask)
    {
        _logger.LogDebug("HandleProcessingTaskCompletion entered. Task Status: {Status}, TaskId: {TaskId}", completedTask.Status, completedTask.Id);
        await _stateLock.WaitAsync(CancellationToken.None);
        try
        {
            if ( completedTask != _activeProcessingTask )
            {
                _logger.LogWarning("HandleProcessingTaskCompletion: Stale continuation detected. Ignoring.");

                return;
            }

            var finalState = ConversationState.Idle;
            switch ( completedTask.Status )
            {
                case TaskStatus.Faulted:         _logger.LogError(completedTask.Exception?.Flatten().InnerExceptions.FirstOrDefault(), "LLM processing task failed."); break;
                case TaskStatus.Canceled:        _logger.LogInformation("LLM processing task was cancelled."); break;
                case TaskStatus.RanToCompletion: _logger.LogDebug("LLM processing task completed successfully."); break;
                default:                         _logger.LogWarning("HandleProcessingTaskCompletion: Unexpected task status {Status}", completedTask.Status); break;
            }

            await TransitionToStateAsync(finalState);
            _logger.LogDebug("HandleProcessingTaskCompletion: Clearing active processing task reference.");
            _activeProcessingTask = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error within HandleProcessingTaskCompletion continuation.");
            try
            {
                _activeProcessingTask = null;
                await TransitionToStateAsync(ConversationState.Idle);
            }
            catch (Exception recoveryEx)
            {
                _logger.LogError(recoveryEx, "Failed to recover state to Idle within HandleProcessingTaskCompletion catch block.");
            }
        }
        finally
        {
            _stateLock.Release();
            _logger.LogDebug("HandleProcessingTaskCompletion finished.");
        }
    }

    // --- MODIFIED ProcessLlmRequestAsync to publish speaking events ---
    private async Task ProcessLlmRequestAsync(UserUtteranceCompleted utterance, CancellationToken cancellationToken)
    {
        _logger.LogDebug("ProcessLlmRequestAsync entered for utterance from '{User}'", utterance.User);
        var stopwatch = Stopwatch.StartNew();
        _firstTokenTcs ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var currentFirstTokenTcs = _firstTokenTcs;
        var stopReason           = "Unknown"; // Track reason for stopping

        try
        {
            _logger.LogDebug("Requesting LLM stream...");
            var textStream = _llmEngine.GetStreamingChatResponseAsync( /*...*/); // Assume params are correct as in Step 2

            var (firstTokenDetectedStream, firstTokenTask) = WrapWithFirstTokenDetection(textStream, stopwatch, currentFirstTokenTcs, cancellationToken);
            var stateTransitionTask = firstTokenTask.ContinueWith(async _ =>
                                                                  {
                                                                      /* ... State transition logic ... */
                                                                  }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

            _logger.LogDebug("Requesting TTS stream...");
            var audioSegments       = _ttsSynthesizer.SynthesizeStreamingAsync(firstTokenDetectedStream, cancellationToken: cancellationToken);
            var latencyTrackedAudio = WrapAudioSegments(audioSegments, utterance.StartTimestamp, cancellationToken);

            // --- Publish AssistantSpeakingStarted ---
            _logger.LogDebug("Publishing AssistantSpeakingStarted event.");
            await _systemStateWriter.WriteAsync(new AssistantSpeakingStarted(DateTimeOffset.Now), cancellationToken);
            // --- End Publish ---

            _logger.LogDebug("Starting audio playback...");
            await AudioPlayer.StartPlaybackAsync(latencyTrackedAudio, cancellationToken);

            // Playback completed normally
            stopReason = "CompletedNaturally";
            _logger.LogInformation("Audio playback completed naturally for utterance from '{User}'.", utterance.User);

            // Wait for state transition task if first token arrived
            try
            {
                await stateTransitionTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                /* Expected if cancelled before first token */
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopReason = "Cancelled"; // Update stop reason
            _logger.LogInformation("ProcessLlmRequestAsync cancelled for utterance from '{User}'.", utterance.User);
            currentFirstTokenTcs.TrySetCanceled(cancellationToken);

            throw;
        }
        catch (Exception ex)
        {
            stopReason = "Error"; // Update stop reason
            _logger.LogError(ex, "Error during LLM processing, TTS, or playback within ProcessLlmRequestAsync for utterance from '{User}'.", utterance.User);
            currentFirstTokenTcs.TrySetException(ex);

            throw;
        }
        finally
        {
            // --- Publish AssistantSpeakingStopped ---
            _logger.LogDebug("Publishing AssistantSpeakingStopped event. Reason: {Reason}", stopReason);
            // Use TryWrite as this is finally block, don't want to hang here.
            // Consider if WriteAsync with timeout/cancellation is better.
            _systemStateWriter.TryWrite(new AssistantSpeakingStopped(DateTimeOffset.Now, stopReason));
            // --- End Publish ---

            stopwatch.Stop();
            _logger.LogDebug("ProcessLlmRequestAsync finished execution for utterance from '{User}'. Elapsed: {Elapsed}ms", utterance.User, stopwatch.ElapsedMilliseconds);
            if ( _firstTokenTcs == currentFirstTokenTcs && _firstTokenTcs.Task.IsCompleted )
            {
                _firstTokenTcs = null;
            }
        }
    }
    // --- END MODIFIED ---


    // --- WrapWithFirstTokenDetection & WrapAudioSegments (Same as Step 2) ---
    private (IAsyncEnumerable<string> Stream, Task FirstTokenTask) WrapWithFirstTokenDetection(
        IAsyncEnumerable<string> source, Stopwatch stopwatch, TaskCompletionSource<bool> tcs, CancellationToken cancellationToken)
    {
        // ... (Implementation from Step 1) ...
        async IAsyncEnumerable<string> WrappedStream([EnumeratorCancellation] CancellationToken ct = default) {
             var firstChunkProcessed = false;
            await foreach (var chunk in source.WithCancellation(ct)) {
                 ct.ThrowIfCancellationRequested();
                 if (!firstChunkProcessed && !string.IsNullOrEmpty(chunk)) {
                     if (tcs.TrySetResult(true)) {
                         stopwatch.Stop();
                         _logger.LogInformation("First token latency: {Latency}ms", stopwatch.ElapsedMilliseconds);
                     }
                     firstChunkProcessed = true;
                 }
                 yield return chunk;
             }
             if (!firstChunkProcessed) {
                  _logger.LogWarning("LLM stream completed without yielding any non-empty chunks.");
                  tcs.TrySetCanceled(cancellationToken.IsCancellationRequested ? cancellationToken : new CancellationToken(true));
             }
        }
        return (WrappedStream(cancellationToken), tcs.Task);
    }

    private async IAsyncEnumerable<AudioSegment> WrapAudioSegments(
        IAsyncEnumerable<AudioSegment> source, DateTimeOffset? utteranceStartTime, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // ... (Implementation from Step 1) ...
         var firstSegment = true;
         await foreach (var segment in source.WithCancellation(cancellationToken)) {
             cancellationToken.ThrowIfCancellationRequested();
             if (firstSegment) {
                 firstSegment = false;
                 if (utteranceStartTime.HasValue) {
                     var latency = DateTimeOffset.Now - utteranceStartTime.Value;
                     _logger.LogInformation("End-to-end latency (to first audio segment): {Latency}ms", latency.TotalMilliseconds);
                 } else {
                      _logger.LogWarning("Could not calculate end-to-end latency, utterance start time not available.");
                 }
             }
             yield return segment;
         }
    }
    // --- End unchanged methods ---


    // --- TriggerBargeInAsync, CancelLlmProcessingAsync, TransitionToStateAsync (Same as Step 2) ---
    private async Task TriggerBargeInAsync(CancellationToken cancellationToken)
    {
        // ... (Implementation from Step 1) ...
         _logger.LogDebug("TriggerBargeInAsync entered.");
         await _stateLock.WaitAsync(cancellationToken);
         try {
             if (_state != ConversationState.Streaming) {
                 _logger.LogWarning("Barge-in trigger attempted but state was {State}. Aborting.", _state);
                 return;
             }
             _logger.LogInformation("Triggering Barge-In: Transitioning to Cancelling, stopping audio, cancelling LLM.");
             await TransitionToStateAsync(ConversationState.Cancelling);
             var stopAudioTask = AudioPlayer.StopPlaybackAsync();
             var cancelLlmTask = CancelLlmProcessingAsync();
             await Task.WhenAll(stopAudioTask, cancelLlmTask).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
         } catch (OperationCanceledException) { _logger.LogWarning("Operation cancelled during TriggerBargeInAsync."); }
         catch (TimeoutException) { _logger.LogWarning("Timeout during TriggerBargeInAsync while stopping audio/cancelling LLM."); }
         catch (Exception ex) { _logger.LogError(ex, "Error during barge-in trigger execution."); }
         finally { _stateLock.Release(); }
         _logger.LogDebug("TriggerBargeInAsync finished.");
    }

    private async Task CancelLlmProcessingAsync(bool stopAudio = false)
    {
        // ... (Implementation from Step 1) ...
        _logger.LogInformation("Attempting to cancel current LLM processing... StopAudio={StopAudio}", stopAudio);
        CancellationTokenSource? ctsToCancel = _activeLlmCts; // Read outside lock
        Task? taskToWait = _activeProcessingTask; // Read outside lock

        if (!ctsToCancel.IsCancellationRequested) {
            _logger.LogDebug("Signalling cancellation via CancellationTokenSource (Instance: {HashCode}).", ctsToCancel.GetHashCode());
            try { ctsToCancel.Cancel(); }
            catch (ObjectDisposedException) {
                 _logger.LogWarning("Attempted to cancel an already disposed CancellationTokenSource.");
                 ctsToCancel = null; taskToWait = null;
            }
        } else { _logger.LogDebug("Cancellation already requested or no active CTS to cancel."); }

        if (stopAudio) {
            _logger.LogDebug("Explicitly stopping audio playback due to cancellation request.");
            try { await AudioPlayer.StopPlaybackAsync().WaitAsync(TimeSpan.FromSeconds(1)); }
            catch (Exception ex) { _logger.LogError(ex, "Error during explicit audio stop on cancellation."); }
        }

        if (taskToWait != null && ctsToCancel != null && !taskToWait.IsCompleted) {
            _logger.LogDebug("Waiting briefly for active processing task ({TaskId}) to observe cancellation...", taskToWait.Id);
            await Task.WhenAny(taskToWait, Task.Delay(150));
             _logger.LogDebug("Brief wait completed. Task Status: {Status}", taskToWait.Status);
        } else { _logger.LogDebug("No active/incomplete task found to wait for, or no CTS was cancelled/valid."); }
    }


    private Task TransitionToStateAsync(ConversationState newState)
    {
        // ... (Implementation from Step 1, lock assumed held by caller or handled internally if needed) ...
        var oldState = _state;
        if (oldState == newState)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("State transition: {OldState} -> {NewState}", oldState, newState);
        _state = newState;

        var wasStreaming = _isInStreamingState;
        _isInStreamingState = newState == ConversationState.Streaming;

        if (wasStreaming && !_isInStreamingState)
        {
            _logger.LogDebug("Exiting Streaming state, resetting barge-in tracking.");
        }
        return Task.CompletedTask;
    }
    // --- End unchanged methods ---


    // --- Enums and Records (Keep as is) ---
    private enum ConversationState
    {
        Idle,
        Processing,
        Streaming,
        Cancelling
    }
    // --- END Enums and Records ---
}