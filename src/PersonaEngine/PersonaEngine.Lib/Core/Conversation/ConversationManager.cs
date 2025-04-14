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

    // --- REMOVED Pending Transcript ---
    // private readonly StringBuilder _pendingTranscript = new();
    // --- END REMOVED ---

    // Keep potential barge-in text temporarily
    private readonly StringBuilder _potentialBargeInText = new();

    // Task to process incoming events (both potential transcript and completed utterances)
    private readonly Task          _eventProcessingTask;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    // --- MODIFIED Channel Readers ---
    private readonly ChannelReader<ITranscriptionEvent>    _transcriptionEventReader;  // Still needed for PotentialTranscriptUpdate
    private readonly ChannelReader<UserUtteranceCompleted> _utteranceCompletionReader; // NEW
    // --- END MODIFIED ---

    // TTS, VisualQA remain
    private readonly ITtsEngine       _ttsSynthesizer;
    private readonly IVisualQAService _visualQaService;

    // State variables remain largely the same for now
    private CancellationTokenSource     _activeLlmCts = new();
    private Task?                       _activeProcessingTask;
    private string                      _currentSpeaker = "User"; // Will be set by UserUtteranceCompleted event
    private TaskCompletionSource<bool>? _firstTokenTcs;
    private bool                        _isInStreamingState = false;
    private DateTimeOffset?             _potentialBargeInStartTime; // Keep for now
    private ConversationState           _state = ConversationState.Idle;

    // --- REMOVED Transcription Start Timestamp ---
    // private DateTimeOffset? _transcriptionStartTimestamp; // Now part of UserUtteranceCompleted
    // --- END REMOVED ---
    
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

        // --- Get BOTH readers from the registry ---
        _transcriptionEventReader  = channelRegistry?.TranscriptionEvents.Reader ?? throw new ArgumentNullException(nameof(channelRegistry));
        _utteranceCompletionReader = channelRegistry?.UtteranceCompletionEvents.Reader ?? throw new ArgumentNullException(nameof(channelRegistry));
        // --- END Get Readers ---
        
        _visualQaService.StartAsync().ConfigureAwait(false);

        // --- Start single task to process BOTH event types ---
        _eventProcessingTask = ProcessIncomingEventsAsync(_mainCts.Token);
        // --- END Start Task ---

        _logger.LogInformation("ConversationManager initialized and ready");
    }
    
    public IAggregatedStreamingAudioPlayer AudioPlayer { get; }
    
    public async ValueTask DisposeAsync()
    {
        // Dispose logic remains similar, waits for the single event processing task
        _logger.LogInformation("Disposing ConversationManager...");
        try
        {
            if (!_mainCts.IsCancellationRequested)
            {
                await _mainCts.CancelAsync();
            }

            await CancelLlmProcessingAsync(true);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await Task.WhenAll(_eventProcessingTask).WaitAsync(timeoutCts.Token); // Wait for the single task
            }
            catch (OperationCanceledException) { _logger.LogWarning("Timeout waiting for task during disposal."); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Error during task wait in disposal."); }
        }
        catch (OperationCanceledException) { /* Expected */ }
        catch (Exception ex) { _logger.LogError(ex, "Error during ConversationManager disposal"); }
        finally
        {
            _stateLock.Dispose();
            _activeLlmCts.Dispose();
            _mainCts.Dispose();
        }
        _logger.LogInformation("ConversationManager disposed successfully");
    }

    public void Execute(GL gl) { }

    private async Task ProcessIncomingEventsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting incoming event processing loop (Utterances & Potential Transcripts)");

        // Combine reading from both channels using Task.WhenAny
        var transcriptionTask = ReadTranscriptionEventsAsync(cancellationToken);
        var utteranceTask = ReadUtteranceEventsAsync(cancellationToken);

        var tasks = new List<Task> { transcriptionTask, utteranceTask };

        try
        {
            while (!cancellationToken.IsCancellationRequested && tasks.Count > 0)
            {
                // Wait for *any* of the channel reading tasks to produce a result or complete
                var completedTask = await Task.WhenAny(tasks);

                if (completedTask == transcriptionTask)
                {
                    // Process result from transcription channel (PotentialTranscriptUpdate)
                    var (event_, completed) = await transcriptionTask;
                    if (event_ is PotentialTranscriptUpdate potential)
                    {
                        await HandlePotentialTranscriptAsync(potential, cancellationToken);
                    }

                    if (completed)
                    {
                         _logger.LogInformation("TranscriptionEvents channel completed.");
                         tasks.Remove(transcriptionTask); // Remove completed task
                         transcriptionTask = null; // Avoid re-adding
                    }
                    else
                    {
                        // Restart reading from this channel for the next event
                         if(transcriptionTask != null) tasks.Remove(transcriptionTask);
                         transcriptionTask = ReadTranscriptionEventsAsync(cancellationToken);
                         tasks.Add(transcriptionTask);
                    }
                }
                else if (completedTask == utteranceTask)
                {
                    // Process result from utterance channel (UserUtteranceCompleted)
                     var (event_, completed) = await utteranceTask;
                    if (event_ is UserUtteranceCompleted utterance)
                    {
                        await HandleCompletedUtteranceAsync(utterance, cancellationToken);
                    }
                     if (completed)
                     {
                         _logger.LogInformation("UtteranceCompletionEvents channel completed.");
                         tasks.Remove(utteranceTask); // Remove completed task
                         utteranceTask = null; // Avoid re-adding
                     }
                     else
                     {
                         // Restart reading from this channel for the next event
                          if(utteranceTask != null) tasks.Remove(utteranceTask);
                          utteranceTask = ReadUtteranceEventsAsync(cancellationToken);
                          tasks.Add(utteranceTask);
                     }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
             _logger.LogInformation("Event processing loop cancelled via token.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in combined event processing loop.");
        }
        finally
        {
             _logger.LogInformation("Combined event processing loop finished.");
        }
    }
    
    private async Task<(ITranscriptionEvent?, bool completed)> ReadTranscriptionEventsAsync(CancellationToken ct)
    {
        try
        {
            if (await _transcriptionEventReader.WaitToReadAsync(ct))
            {
                if (_transcriptionEventReader.TryRead(out var item))
                {
                    return (item, false);
                }
            }
        }
        catch (OperationCanceledException) { /* Expected */ }
        catch (ChannelClosedException) { /* Expected */ }

        return (null, true); // Channel completed or cancelled
    }
    
    private async Task<(UserUtteranceCompleted?, bool completed)> ReadUtteranceEventsAsync(CancellationToken ct)
    {
        try
        {
            if (await _utteranceCompletionReader.WaitToReadAsync(ct))
            {
                if (_utteranceCompletionReader.TryRead(out var item))
                {
                    return (item, false);
                }
            }
        }
        catch (OperationCanceledException) { /* Expected */ }
        catch (ChannelClosedException) { /* Expected */ }

        return (null, true); // Channel completed or cancelled
    }
    
    // --- Method to handle potential transcripts (for barge-in/cancellation ONLY) ---
    private async Task HandlePotentialTranscriptAsync(PotentialTranscriptUpdate potentialSegment, CancellationToken cancellationToken)
    {
        _logger.LogTrace("Recognizing: Source='{SourceId}', Text='{Text}'", potentialSegment.SourceId, potentialSegment.PartialText);

        // --- Barge-in / Pre-first-token cancellation logic (STILL TEMPORARY) ---
        if (_isInStreamingState) // If AI is speaking
        {
            if (string.IsNullOrWhiteSpace(potentialSegment.PartialText)) return;

            if (_potentialBargeInStartTime == null)
            {
                _potentialBargeInStartTime = DateTimeOffset.Now;
                _potentialBargeInText.Clear();
                _logger.LogDebug("Potential barge-in started.");
            }

            _potentialBargeInText.Clear().Append(potentialSegment.PartialText);
            var bargeInDuration = DateTimeOffset.Now - _potentialBargeInStartTime.Value;

            const int BargeInDetectionMinLength = 5; // Define constants or move to config
            TimeSpan BargeInDetectionDuration = TimeSpan.FromMilliseconds(400);

            if (bargeInDuration >= BargeInDetectionDuration && _potentialBargeInText.Length >= BargeInDetectionMinLength)
            {
                _logger.LogInformation("Barge-in detected: Duration={Duration}ms, Length={Length}", bargeInDuration.TotalMilliseconds, _potentialBargeInText.Length);
                await TriggerBargeInAsync(cancellationToken); // Await here? Or fire/forget? Let's await briefly.
                _potentialBargeInStartTime = null;
                _potentialBargeInText.Clear();
            }
        }
        else // AI is not speaking
        {
            _potentialBargeInStartTime = null;
            _potentialBargeInText.Clear();
        }

        // Cancel if processing and new speech before first token
        if (_state == ConversationState.Processing && _firstTokenTcs is { Task.IsCompleted: false })
        {
            _logger.LogDebug("Cancelling LLM call due to new speech before first token (based on potential transcript)");
            await CancelLlmProcessingAsync(); // Await cancellation attempt
        }
        // --- END TEMPORARY Logic ---
    }
    
    // --- Method to handle completed utterances ---
    private async Task HandleCompletedUtteranceAsync(UserUtteranceCompleted utterance, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received completed utterance: Source='{SourceId}', User='{User}', Length={Length}, Start='{Start}', End='{End}'",
                             utterance.SourceId, utterance.User, utterance.AggregatedText.Length, utterance.StartTimestamp, utterance.EndTimestamp);

        // Reset potential barge-in state now that a full utterance is finalized
        _potentialBargeInStartTime = null;
        _potentialBargeInText.Clear();

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            var currentState = _state;
            _logger.LogDebug("HandleCompletedUtteranceAsync entered. Current state: {State}", currentState);

            switch (currentState)
            {
                case ConversationState.Idle:
                    if (!string.IsNullOrWhiteSpace(utterance.AggregatedText))
                    {
                        _logger.LogInformation("Idle state: Processing completed utterance.");
                        _currentSpeaker = utterance.User; // Set current speaker
                        await TransitionToStateAsync(ConversationState.Processing);
                        // Start processing task, passing utterance data directly
                        _ = StartLlmProcessingTaskAsync(utterance, CancellationToken.None);
                    }
                    else
                    {
                         _logger.LogWarning("Idle state: Received completed utterance with empty text. Ignoring.");
                    }
                    break;

                case ConversationState.Processing:
                case ConversationState.Streaming:
                case ConversationState.Cancelling:
                    // If already busy, the new utterance might need to be queued or handled
                    // based on conversation strategy. For now, we log it.
                    // The HandleProcessingTaskCompletion logic might need adjustment
                    // to check for a "queued" next utterance instead of _pendingTranscript.
                    // --- SIMPLE APPROACH FOR NOW: Log and potentially drop/ignore ---
                    // (More complex handling requires an explicit queue or different state machine)
                    _logger.LogWarning("Received completed utterance while in state {State}. Utterance from '{User}' ignored for now: '{Text}'",
                                       currentState, utterance.User, utterance.AggregatedText.Substring(0, Math.Min(utterance.AggregatedText.Length, 50)));
                    // TODO: Implement queuing or alternative handling strategy for concurrent utterances.
                    break;
            }
        }
        finally
        {
            _stateLock.Release();
            _logger.LogDebug("HandleCompletedUtteranceAsync finished.");
        }
    }
    
    // --- REMOVED HandleTranscriptionSegmentAsync ---
    // private async Task HandleTranscriptionSegmentAsync(CancellationToken cancellationToken) { ... }
    // --- END REMOVED ---


    // --- MODIFIED StartLlmProcessingTaskAsync to accept utterance ---
    private Task StartLlmProcessingTaskAsync(UserUtteranceCompleted utterance, CancellationToken externalCancellationToken)
    {
        return Task.Run(async () =>
        {
            try
            {
                _logger.LogDebug("Task.Run: Executing StartLlmProcessingAsync for utterance from {User}.", utterance.User);
                await StartLlmProcessingAsync(utterance, externalCancellationToken);
                _logger.LogDebug("Task.Run: StartLlmProcessingAsync completed.");
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Unhandled exception directly within StartLlmProcessingAsync execution task for utterance from {User}.", utterance.User);
                await _stateLock.WaitAsync(CancellationToken.None);
                try
                {
                    _logger.LogWarning("Attempting to reset state to Idle due to StartLlmProcessingAsync task failure.");
                    // No pending transcript to clear
                    _activeProcessingTask = null;
                    await TransitionToStateAsync(ConversationState.Idle);
                }
                catch (Exception lockEx) { _logger.LogError(lockEx, "Failed to acquire lock or reset state after StartLlmProcessingAsync task failure."); }
                finally { if (_stateLock.CurrentCount == 0) _stateLock.Release(); }
            }
        });
    }
    // --- END MODIFIED ---

    // --- MODIFIED StartLlmProcessingAsync to accept utterance ---
    private async Task StartLlmProcessingAsync(UserUtteranceCompleted utterance, CancellationToken externalCancellationToken)
    {
        _logger.LogDebug("StartLlmProcessingAsync entered for completed utterance.");
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_mainCts.Token, externalCancellationToken);
        var combinedToken = linkedCts.Token;

        // --- State management under lock ---
        await _stateLock.WaitAsync(combinedToken);
        try
        {
             // Check if still in Processing state - could have been cancelled between event handling and here
             if(_state != ConversationState.Processing)
             {
                 _logger.LogWarning("StartLlmProcessingAsync: State changed to {State} before processing could start. Aborting.", _state);
                 return;
             }

             _logger.LogInformation("StartLlmProcessingAsync: Processing utterance ({Length} chars) from '{User}': '{Text}'",
                                    utterance.AggregatedText.Length, utterance.User,
                                    utterance.AggregatedText.Substring(0, Math.Min(utterance.AggregatedText.Length, 100)));

             // Dispose old CTS and create new one *within the lock*
            _activeLlmCts?.Dispose();
            _activeLlmCts = CancellationTokenSource.CreateLinkedTokenSource(combinedToken);
        }
        finally
        {
             _stateLock.Release();
        }
        // --- End State management ---

        var llmCancellationToken = _activeLlmCts.Token; // Get token after creating CTS

        Task? currentProcessingTask = null;
        try
        {
            _logger.LogDebug("StartLlmProcessingAsync: Calling ProcessLlmRequestAsync.");
            // Pass utterance details needed by ProcessLlmRequestAsync
            currentProcessingTask = ProcessLlmRequestAsync(utterance, llmCancellationToken);

            // --- Assign active task immediately ---
             await _stateLock.WaitAsync(combinedToken); // Re-acquire lock briefly
             try
             {
                // Only assign if still in processing state and no other task took over
                 if (_state == ConversationState.Processing && _activeProcessingTask == null)
                 {
                     _activeProcessingTask = currentProcessingTask;
                     _logger.LogDebug("StartLlmProcessingAsync: ProcessLlmRequestAsync called, task assigned (Id: {TaskId}).", currentProcessingTask.Id);
                 }
                 else
                 {
                      _logger.LogWarning("StartLlmProcessingAsync: Could not assign active task. State: {State}, Existing Task: {TaskId}. Attempting cancellation of new task.", _state, _activeProcessingTask?.Id);
                      // Cancel the task we just created as it won't be tracked
                      _activeLlmCts.Cancel(); // Cancel the specific CTS for this orphaned task
                      throw new OperationCanceledException("State changed before LLM task could be tracked.");
                 }
             }
             finally
             {
                 _stateLock.Release();
             }
            // --- End Assign active task ---


            _logger.LogDebug("StartLlmProcessingAsync: Setting up continuation task.");
            _ = currentProcessingTask.ContinueWith(
                continuationAction: task => _ = HandleProcessingTaskCompletion(task), // Fire-and-forget handler
                cancellationToken: CancellationToken.None,
                continuationOptions: TaskContinuationOptions.ExecuteSynchronously,
                scheduler: TaskScheduler.Default
            );
            _logger.LogDebug("StartLlmProcessingAsync: Continuation task setup complete.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) // Don't log OperationCanceledException here if it's expected state change
        {
            _logger.LogError(ex, "StartLlmProcessingAsync: Synchronous exception during ProcessLlmRequestAsync call or task assignment/continuation setup.");
            await _stateLock.WaitAsync(CancellationToken.None);
            try
            {
                _logger.LogWarning("StartLlmProcessingAsync: Resetting state to Idle due to synchronous startup failure.");
                 // Check if the failed task is the active one before clearing
                 if (_activeProcessingTask == currentProcessingTask)
                 {
                    _activeProcessingTask = null;
                 }
                 // Only transition if we are still in a processing related state
                 if (_state == ConversationState.Processing || _state == ConversationState.Cancelling)
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
    // --- END MODIFIED ---


    // --- MODIFIED HandleProcessingTaskCompletion ---
    private async Task HandleProcessingTaskCompletion(Task completedTask)
    {
        _logger.LogDebug("HandleProcessingTaskCompletion entered. Task Status: {Status}, TaskId: {TaskId}", completedTask.Status, completedTask.Id);
        await _stateLock.WaitAsync(CancellationToken.None);
        try
        {
            if (completedTask != _activeProcessingTask)
            {
                _logger.LogWarning("HandleProcessingTaskCompletion: Stale continuation detected for TaskId {CompletedTaskId}. Current active TaskId is {ActiveTaskId}. Ignoring.",
                                   completedTask.Id, _activeProcessingTask?.Id ?? -1);
                return;
            }

            // --- Logic change: No pending transcript to check ---
            // Instead, if concurrent utterances arrived, they were logged/ignored in HandleCompletedUtteranceAsync.
            // The system simply transitions back to Idle unless an error occurred.
            // Future Orchestrator would handle queuing.

            var finalState = ConversationState.Idle; // Default to Idle after completion/cancellation/failure

            switch (completedTask.Status)
            {
                case TaskStatus.Faulted:
                    _logger.LogError(completedTask.Exception?.Flatten().InnerExceptions.FirstOrDefault(), "LLM processing task failed.");
                    break;

                case TaskStatus.Canceled:
                    _logger.LogInformation("LLM processing task was cancelled.");
                    // Currently, cancellation leads directly back to Idle.
                    // An Orchestrator might check a queue here.
                    break;

                case TaskStatus.RanToCompletion:
                    _logger.LogDebug("LLM processing task completed successfully.");
                    // Leads back to Idle.
                    break;

                default:
                    _logger.LogWarning("HandleProcessingTaskCompletion: Unexpected task status {Status}", completedTask.Status);
                    break;
            }

            await TransitionToStateAsync(finalState);

            // Clear the active task reference now that this one is handled
            _logger.LogDebug("HandleProcessingTaskCompletion: Clearing active processing task reference.");
            _activeProcessingTask = null;

            // --- REMOVED Restart logic based on _pendingTranscript ---
            // if (restartProcessing) { ... }
            // --- END REMOVED ---
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error within HandleProcessingTaskCompletion continuation.");
            try
            {
                // Attempt recovery to Idle state
                _activeProcessingTask = null;
                await TransitionToStateAsync(ConversationState.Idle);
            }
            catch (Exception recoveryEx) { _logger.LogError(recoveryEx, "Failed to recover state to Idle within HandleProcessingTaskCompletion catch block."); }
        }
        finally
        {
            _stateLock.Release();
            _logger.LogDebug("HandleProcessingTaskCompletion finished.");
        }
    }
    // --- END MODIFIED ---


    // --- MODIFIED ProcessLlmRequestAsync to accept utterance ---
    private async Task ProcessLlmRequestAsync(UserUtteranceCompleted utterance, CancellationToken cancellationToken)
    {
        _logger.LogDebug("ProcessLlmRequestAsync entered for utterance from '{User}'", utterance.User);
        var stopwatch = Stopwatch.StartNew();
        _firstTokenTcs ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var currentFirstTokenTcs = _firstTokenTcs;

        try
        {
            _logger.LogDebug("Requesting LLM stream...");
            var textStream = _llmEngine.GetStreamingChatResponseAsync(
                new ChatMessage(utterance.User, utterance.AggregatedText), // Use data from utterance
                new InjectionMetadata(
                    _topics.AsReadOnly(),
                    _context,
                    _visualQaService.ScreenCaption ?? string.Empty),
                cancellationToken: cancellationToken);

            // Pass the TCS instance for this specific call
            var (firstTokenDetectedStream, firstTokenTask) = WrapWithFirstTokenDetection(textStream, stopwatch, currentFirstTokenTcs, cancellationToken);

            // State transition logic remains the same
            var stateTransitionTask = firstTokenTask.ContinueWith(async _ =>
            {
                 // ... (state transition logic as before) ...
                 _logger.LogDebug("First token detected task continuation running.");
                await _stateLock.WaitAsync(CancellationToken.None);
                try
                {
                    if (_state == ConversationState.Processing) {
                        _logger.LogInformation("First token received, transitioning state Processing -> Streaming.");
                        await TransitionToStateAsync(ConversationState.Streaming);
                    } else {
                         _logger.LogWarning("First token received, but state was already {State}. No transition.", _state);
                     }
                } finally { _stateLock.Release(); }

            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);


            _logger.LogDebug("Requesting TTS stream...");
            var audioSegments = _ttsSynthesizer.SynthesizeStreamingAsync(firstTokenDetectedStream, cancellationToken: cancellationToken);

            // Pass the utterance start time for latency tracking
            var latencyTrackedAudio = WrapAudioSegments(audioSegments, utterance.StartTimestamp, cancellationToken);

            _logger.LogDebug("Starting audio playback...");
            await AudioPlayer.StartPlaybackAsync(latencyTrackedAudio, cancellationToken);

            // Wait for state transition task if first token arrived
            await stateTransitionTask.WaitAsync(cancellationToken);

            _logger.LogInformation("Audio playback completed naturally for utterance from '{User}'.", utterance.User);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("ProcessLlmRequestAsync cancelled for utterance from '{User}'.", utterance.User);
            currentFirstTokenTcs.TrySetCanceled(cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Error during LLM processing, TTS, or playback within ProcessLlmRequestAsync for utterance from '{User}'.", utterance.User);
             currentFirstTokenTcs.TrySetException(ex);
             throw;
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogDebug("ProcessLlmRequestAsync finished execution for utterance from '{User}'. Elapsed: {Elapsed}ms", utterance.User, stopwatch.ElapsedMilliseconds);
            if (_firstTokenTcs == currentFirstTokenTcs && _firstTokenTcs.Task.IsCompleted)
            {
                 _firstTokenTcs = null;
            }
        }
    }
    // --- END MODIFIED ---


    // --- WrapWithFirstTokenDetection and WrapAudioSegments remain the same as Step 1 ---
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


    // --- TriggerBargeInAsync, CancelLlmProcessingAsync, TransitionToStateAsync remain the same as Step 1 ---
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
            _potentialBargeInStartTime = null;
            _potentialBargeInText.Clear();
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