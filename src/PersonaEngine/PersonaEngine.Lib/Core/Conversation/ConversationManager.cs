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
    // --- REMOVED Constants/Fields related to Transcription ---
    // private const int BargeInDetectionMinLength = 5;
    // private static readonly TimeSpan BargeInDetectionDuration = TimeSpan.FromMilliseconds(400);
    // --- END REMOVED Constants/Fields ---
    
    private readonly string                  _context = "Relaxed discussion in discord voice chat.";
    private readonly IChatEngine             _llmEngine;
    private readonly ILogger                 _logger;
    private readonly CancellationTokenSource _mainCts = new();
    
    // --- REMOVED Microphone and Transcriptor fields ---
    // private readonly IMicrophone _microphone;
    // private readonly IRealtimeSpeechTranscriptor _transcriptor;
    // --- END REMOVED Fields ---
    
    private readonly StringBuilder _pendingTranscript    = new();
    private readonly StringBuilder _potentialBargeInText = new(); // Keep for now, logic needs relocation later
    private readonly Task          _processingTask;               // Renamed from _transcriptionTask
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly List<string>  _topics    = ["casual conversation"];
    
    // --- REMOVED Transcription Channel ---
    // private readonly Channel<TranscriptionSegment> _transcriptionChannel;
    // --- END REMOVED Channel ---
    
    // --- ADDED Channel Reader ---
    private readonly ChannelReader<ITranscriptionEvent> _transcriptionEventReader;
    // --- END ADDED Reader ---
    
    // --- REMOVED Transcription Task Field ---
    // private readonly Task _transcriptionTask; // Replaced by _processingTask reading from channel
    // --- END REMOVED Task ---
    
    private readonly ITtsEngine       _ttsSynthesizer;
    private readonly IVisualQAService _visualQaService;
    
    private CancellationTokenSource     _activeLlmCts = new();
    private Task?                       _activeProcessingTask;
    private string                      _currentSpeaker = "User"; // Default, will be updated from event
    private TaskCompletionSource<bool>? _firstTokenTcs;
    private bool                        _isInStreamingState = false;
    private DateTimeOffset?             _potentialBargeInStartTime; // Keep for now, logic needs relocation later
    private ConversationState           _state = ConversationState.Idle;
    private DateTimeOffset?             _transcriptionStartTimestamp; // Timestamp of the *first* segment of current utterance
    
    public ConversationManagerRefactor(
        // --- REMOVED Microphone and Transcriptor from constructor ---
        // IMicrophone microphone,
        // IRealtimeSpeechTranscriptor transcriptor,
        // --- END REMOVED Parameters ---
        IChatEngine                     llmEngine,
        ITtsEngine                      ttsSynthesizer,
        IVisualQAService                visualQaService,
        IAggregatedStreamingAudioPlayer audioPlayer,
        IChannelRegistry                channelRegistry, // --- ADDED Channel Registry dependency ---
        ILogger<ConversationManager>    logger)
    {
        // --- REMOVED Assignments ---
        // _microphone = microphone ?? throw new ArgumentNullException(nameof(microphone));
        // _transcriptor = transcriptor ?? throw new ArgumentNullException(nameof(transcriptor));
        // --- END REMOVED Assignments ---

        _llmEngine       = llmEngine ?? throw new ArgumentNullException(nameof(llmEngine));
        _ttsSynthesizer  = ttsSynthesizer ?? throw new ArgumentNullException(nameof(ttsSynthesizer));
        _visualQaService = visualQaService ?? throw new ArgumentNullException(nameof(visualQaService));
        AudioPlayer      = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
        _logger          = logger ?? throw new ArgumentNullException(nameof(logger));

        // --- ADDED Getting reader from registry ---
        _transcriptionEventReader = channelRegistry?.TranscriptionEvents.Reader ?? throw new ArgumentNullException(nameof(channelRegistry));
        // --- END ADDED Reader ---

        // --- REMOVED Transcription Channel Creation ---
        // _transcriptionChannel = Channel.CreateBounded<TranscriptionSegment>(
        //      new BoundedChannelOptions(100) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        // --- END REMOVED Channel Creation ---

        _visualQaService.StartAsync().ConfigureAwait(false); // Keep this

        // --- REMOVED Starting transcription loop here ---
        // _transcriptionTask = RunTranscriptionLoopAsync(_mainCts.Token);
        // --- END REMOVED Task Start ---

        // --- MODIFIED Task Start: Now processes events from the channel ---
        _processingTask = ProcessTranscriptionEventsAsync(_mainCts.Token);
        // --- END MODIFIED Task Start ---


        _logger.LogInformation("ConversationManager initialized and ready");
    }
    
    public IAggregatedStreamingAudioPlayer AudioPlayer { get; }
    
    public async ValueTask DisposeAsync()
    {
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
                // --- MODIFIED Task Wait: Wait for the processing task ---
                await Task.WhenAll(/* REMOVED: _transcriptionTask, */ _processingTask).WaitAsync(timeoutCts.Token);
                // --- END MODIFIED Task Wait ---
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Timeout waiting for tasks during disposal.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during task wait in disposal.");
            }
        }
        catch (OperationCanceledException) { /* Expected */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during ConversationManager disposal");
        }
        finally
        {
            _stateLock.Dispose();
            _activeLlmCts.Dispose();
            _mainCts.Dispose();
            // --- REMOVED Channel Completion ---
            // _transcriptionChannel.Writer.TryComplete();
            // --- END REMOVED Channel Completion ---
        }
        _logger.LogInformation("ConversationManager disposed successfully");
    }

    public void Execute(GL gl) { } // Keep as is

    // --- REMOVED RunTranscriptionLoopAsync ---
    // private async Task RunTranscriptionLoopAsync(CancellationToken cancellationToken) { ... }
    // --- END REMOVED RunTranscriptionLoopAsync ---

    // --- RENAMED and MODIFIED: ProcessTranscriptionEventsAsync ---
    private async Task ProcessTranscriptionEventsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting transcription event processing loop");

        try
        {
            // Read from the shared channel provided by IChannelRegistry
            await foreach (var event_ in _transcriptionEventReader.ReadAllAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (event_)
                {
                    case FinalTranscriptSegmentReceived finalSegment:
                        _logger.LogDebug("Received final transcript segment: Source='{SourceId}', User='{User}', Text='{Text}'",
                                         finalSegment.SourceId, finalSegment.User, finalSegment.Text);

                        // --- Append logic remains similar, but uses event data ---
                        if (_pendingTranscript.Length == 0)
                        {
                            _transcriptionStartTimestamp = finalSegment.Timestamp;
                            _currentSpeaker = finalSegment.User; // Update speaker from event
                        }
                        // TODO: Handle segments from different SourceIds if needed (e.g., clear pending if SourceId changes?)
                        _pendingTranscript.Append(finalSegment.Text).Append(" "); // Add space between segments

                        // --- Reset potential barge-in on final segment ---
                        _potentialBargeInStartTime = null;
                        _potentialBargeInText.Clear();
                        // --- End Reset ---

                        // Trigger state handling based on the accumulated transcript
                        await HandleTranscriptionSegmentAsync(cancellationToken);
                        break;

                    case PotentialTranscriptUpdate potentialSegment:
                        _logger.LogTrace("Recognizing: Source='{SourceId}', Text='{Text}'", potentialSegment.SourceId, potentialSegment.PartialText);

                        // --- Barge-in / Pre-first-token cancellation logic (NEEDS REFINEMENT/RELOCATION) ---
                        // This logic is complex and likely belongs in dedicated components
                        // (BargeInDetector, Orchestrator) later in the refactoring.
                        // Keeping a simplified version here temporarily.

                        if (_isInStreamingState) // If AI is speaking
                        {
                            if (string.IsNullOrWhiteSpace(potentialSegment.PartialText)) continue;

                            if (_potentialBargeInStartTime == null)
                            {
                                _potentialBargeInStartTime = DateTimeOffset.Now;
                                _potentialBargeInText.Clear();
                                _logger.LogDebug("Potential barge-in started.");
                            }

                            // Use the latest partial text for barge-in check
                            _potentialBargeInText.Clear().Append(potentialSegment.PartialText);
                            var bargeInDuration = DateTimeOffset.Now - _potentialBargeInStartTime.Value;

                            // Simplified check (constants would need to be defined again or moved)
                            const int BargeInDetectionMinLength = 5;
                            TimeSpan BargeInDetectionDuration = TimeSpan.FromMilliseconds(400);

                            if (bargeInDuration >= BargeInDetectionDuration && _potentialBargeInText.Length >= BargeInDetectionMinLength)
                            {
                                _logger.LogInformation("Barge-in detected: Duration={Duration}ms, Length={Length}", bargeInDuration.TotalMilliseconds, _potentialBargeInText.Length);
                                // Fire and forget is okay here for now, but better handled by Orchestrator later
                                _ = TriggerBargeInAsync(cancellationToken);
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
                             // Don't await here, let the loop continue immediately
                             _ = CancelLlmProcessingAsync(); // Keep this cancellation trigger
                        }
                        // --- END Barge-in / Pre-first-token cancellation logic ---
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Transcription event processing cancelled via main token.");
        }
        catch (ChannelClosedException ex)
        {
            // This happens normally when the writer (TranscriptionService) completes.
            _logger.LogInformation("Transcription event channel closed, ending processing loop. Reason: {Exception}", ex.InnerException?.Message ?? "Channel completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing transcription events");
        }
        finally
        {
            _logger.LogInformation("Transcription event processing loop completed.");
        }
    }
    // --- END RENAMED and MODIFIED ---
    
    // ==============================================================
    // The rest of the methods (HandleTranscriptionSegmentAsync,
    // StartLlmProcessingTaskAsync, StartLlmProcessingAsync,
    // HandleProcessingTaskCompletion, ProcessLlmRequestAsync,
    // WrapWithFirstTokenDetection, WrapAudioSegments,
    // TriggerBargeInAsync, CancelLlmProcessingAsync,
    // TransitionToStateAsync) remain largely the same for now,
    // operating on the _pendingTranscript which is now populated
    // by ProcessTranscriptionEventsAsync reading from the channel.
    //
    // The barge-in logic within ProcessTranscriptionEventsAsync
    // still calls TriggerBargeInAsync.
    // ==============================================================
    
    // --- Helper methods (Keep as is for now) ---
    private async Task HandleTranscriptionSegmentAsync(CancellationToken cancellationToken)
    {
        // This method's logic remains the same for now, triggered when a
        // final segment is received and appended in ProcessTranscriptionEventsAsync
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            var currentState = _state;
            _logger.LogDebug("HandleTranscriptionSegmentAsync entered. Current state: {State}", currentState);

            switch (currentState)
            {
                case ConversationState.Idle:
                    if (_pendingTranscript.Length > 0) // Only process if there's text
                    {
                        _logger.LogInformation("Idle state: Transitioning to Processing and starting LLM task.");
                        await TransitionToStateAsync(ConversationState.Processing);
                        _ = StartLlmProcessingTaskAsync(CancellationToken.None); // Fire and forget task start
                    }
                    else
                    {
                        _logger.LogDebug("Idle state: Received empty final segment, remaining Idle.");
                    }
                    break;

                case ConversationState.Processing:
                case ConversationState.Streaming:
                case ConversationState.Cancelling:
                    // If we are already processing/streaming, the new text in _pendingTranscript
                    // will be handled when the current operation completes and potentially restarts
                    // (logic inside HandleProcessingTaskCompletion).
                    _logger.LogDebug("State is {State}. Appending transcript. Current LLM task will continue or restart upon completion.", currentState);
                    break;
            }
        }
        finally
        {
            _stateLock.Release();
            _logger.LogDebug("HandleTranscriptionSegmentAsync finished.");
        }
    }

    private Task StartLlmProcessingTaskAsync(CancellationToken externalCancellationToken)
    {
        // No changes needed here for Step 1
        return Task.Run(async () =>
        {
            try
            {
                _logger.LogDebug("Task.Run: Executing StartLlmProcessingAsync.");
                await StartLlmProcessingAsync(externalCancellationToken);
                _logger.LogDebug("Task.Run: StartLlmProcessingAsync completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception directly within StartLlmProcessingAsync execution task.");
                await _stateLock.WaitAsync(); // Use CancellationToken.None for cleanup
                try
                {
                    _logger.LogWarning("Attempting to reset state to Idle due to StartLlmProcessingAsync task failure.");
                    _pendingTranscript.Clear();
                    _transcriptionStartTimestamp = null;
                    _activeProcessingTask = null;
                    await TransitionToStateAsync(ConversationState.Idle);
                }
                catch (Exception lockEx)
                {
                    _logger.LogError(lockEx, "Failed to acquire lock or reset state after StartLlmProcessingAsync task failure.");
                }
                finally
                {
                    if (_stateLock.CurrentCount == 0) _stateLock.Release();
                }
            }
        });
    }

    private async Task StartLlmProcessingAsync(CancellationToken externalCancellationToken)
    {
        // No changes needed here for Step 1
        _logger.LogDebug("StartLlmProcessingAsync entered.");
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_mainCts.Token, externalCancellationToken);
        var combinedToken = linkedCts.Token;

        _activeLlmCts?.Dispose();
        _activeLlmCts = CancellationTokenSource.CreateLinkedTokenSource(combinedToken);
        var llmCancellationToken = _activeLlmCts.Token;

        string transcript;
        await _stateLock.WaitAsync(combinedToken);
        try
        {
            // CRITICAL: Take the transcript *now* under lock
            transcript = _pendingTranscript.ToString();
            _pendingTranscript.Clear(); // Clear pending transcript *after* capturing it for processing
            _transcriptionStartTimestamp = null; // Reset timestamp for next utterance

            if (string.IsNullOrWhiteSpace(transcript))
            {
                _logger.LogWarning("StartLlmProcessingAsync: Captured transcript was empty or whitespace. Returning to Idle.");
                await TransitionToStateAsync(ConversationState.Idle);
                return;
            }

            _logger.LogInformation("StartLlmProcessingAsync: Processing transcript ({Length} chars): '{Transcript}'", transcript.Length, transcript.Substring(0, Math.Min(transcript.Length, 100)));
        }
        finally
        {
            _stateLock.Release();
        }

        Task? currentProcessingTask = null;
        try
        {
            _logger.LogDebug("StartLlmProcessingAsync: Calling ProcessLlmRequestAsync.");
            currentProcessingTask = ProcessLlmRequestAsync(transcript, llmCancellationToken); // Pass captured transcript
            _activeProcessingTask = currentProcessingTask;
            _logger.LogDebug("StartLlmProcessingAsync: ProcessLlmRequestAsync called, task assigned.");

            _logger.LogDebug("StartLlmProcessingAsync: Setting up continuation task.");
            _ = currentProcessingTask.ContinueWith(
                continuationAction: task => _ = HandleProcessingTaskCompletion(task), // Fire-and-forget handler
                cancellationToken: CancellationToken.None, // Continuation should run even if original token is cancelled
                continuationOptions: TaskContinuationOptions.ExecuteSynchronously, // Optimize for quick continuations
                scheduler: TaskScheduler.Default
            );
            _logger.LogDebug("StartLlmProcessingAsync: Continuation task setup complete.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StartLlmProcessingAsync: Synchronous exception during ProcessLlmRequestAsync call or task assignment.");
            await _stateLock.WaitAsync(CancellationToken.None); // Use None for cleanup
            try
            {
                _logger.LogWarning("StartLlmProcessingAsync: Resetting state to Idle due to synchronous startup failure.");
                // _pendingTranscript was already cleared or captured
                _activeProcessingTask = null;
                await TransitionToStateAsync(ConversationState.Idle);
            }
            finally
            {
                _stateLock.Release();
            }
        }
        _logger.LogDebug("StartLlmProcessingAsync finished.");
    }


    private async Task HandleProcessingTaskCompletion(Task completedTask)
    {
        // Logic adjusted slightly to handle potential pending transcript *after* completion
        _logger.LogDebug("HandleProcessingTaskCompletion entered. Task Status: {Status}", completedTask.Status);
        await _stateLock.WaitAsync(CancellationToken.None); // Use None for cleanup/state transition
        try
        {
            if (completedTask != _activeProcessingTask)
            {
                _logger.LogWarning("HandleProcessingTaskCompletion: Stale continuation detected for TaskId {CompletedTaskId}. Current active TaskId is {ActiveTaskId}. Ignoring.",
                                   completedTask.Id, _activeProcessingTask?.Id ?? -1);
                return;
            }

            var finalState = ConversationState.Idle;
            var restartProcessing = false;

            switch (completedTask.Status)
            {
                case TaskStatus.Faulted:
                    _logger.LogError(completedTask.Exception?.Flatten().InnerExceptions.FirstOrDefault(), "LLM processing task failed.");
                    // Pending transcript was likely cleared at the start of StartLlmProcessingAsync
                    break;

                case TaskStatus.Canceled:
                    _logger.LogInformation("LLM processing task was cancelled.");
                    // Check if new transcript segments arrived *during* the cancelled processing
                    if (_pendingTranscript.Length > 0)
                    {
                        _logger.LogInformation("HandleProcessingTaskCompletion: Restarting processing after cancellation as new pending transcript exists.");
                        finalState = ConversationState.Processing;
                        restartProcessing = true;
                    }
                    else
                    {
                         _logger.LogInformation("HandleProcessingTaskCompletion: Cancelled with no new pending transcript. Transitioning to Idle.");
                    }
                    break;

                case TaskStatus.RanToCompletion:
                    _logger.LogDebug("LLM processing task completed successfully.");
                     // Check if new transcript segments arrived *during* the successful processing
                    if (_pendingTranscript.Length > 0)
                    {
                        _logger.LogInformation("HandleProcessingTaskCompletion: Restarting processing after successful completion as new pending transcript exists.");
                        finalState = ConversationState.Processing;
                        restartProcessing = true;
                    }
                    break;

                default:
                    _logger.LogWarning("HandleProcessingTaskCompletion: Unexpected task status {Status}", completedTask.Status);
                    break;
            }

            // Transition state *before* potentially restarting
            await TransitionToStateAsync(finalState);

            // Always clear the active task reference now that this one is handled
             _logger.LogDebug("HandleProcessingTaskCompletion: Clearing active processing task reference.");
            _activeProcessingTask = null;


            if (restartProcessing)
            {
                _logger.LogDebug("HandleProcessingTaskCompletion: Initiating processing restart task.");
                // Start the new processing task outside the lock if possible, but okay here too
                _ = StartLlmProcessingTaskAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error within HandleProcessingTaskCompletion continuation.");
            try
            {
                // Attempt recovery to Idle state
                _pendingTranscript.Clear();
                _transcriptionStartTimestamp = null;
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


    private async Task ProcessLlmRequestAsync(string transcript, CancellationToken cancellationToken)
    {
        // No changes needed here for Step 1
        _logger.LogDebug("ProcessLlmRequestAsync entered for transcript: '{StartOfTranscript}...'", transcript.Substring(0, Math.Min(transcript.Length, 50)));
        var stopwatch = Stopwatch.StartNew();
        // Use existing TCS or create new if null
        _firstTokenTcs ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var currentFirstTokenTcs = _firstTokenTcs; // Capture instance for this specific call

        try
        {
            _logger.LogDebug("Requesting LLM stream...");
            var textStream = _llmEngine.GetStreamingChatResponseAsync(
                new ChatMessage(_currentSpeaker, transcript), // Use speaker captured when transcript started
                new InjectionMetadata(
                    _topics.AsReadOnly(),
                    _context,
                    _visualQaService.ScreenCaption ?? string.Empty),
                cancellationToken: cancellationToken);

            var (firstTokenDetectedStream, firstTokenTask) = WrapWithFirstTokenDetection(textStream, stopwatch, currentFirstTokenTcs, cancellationToken);

            var stateTransitionTask = firstTokenTask.ContinueWith(async _ =>
            {
                _logger.LogDebug("First token detected task continuation running.");
                await _stateLock.WaitAsync(CancellationToken.None); // Use None for state update
                try
                {
                    // Check state *before* transitioning
                    if (_state == ConversationState.Processing)
                    {
                        _logger.LogInformation("First token received, transitioning state Processing -> Streaming.");
                        await TransitionToStateAsync(ConversationState.Streaming);
                    }
                    else
                    {
                        _logger.LogWarning("First token received, but state was already {State}. No transition.", _state);
                    }
                }
                finally
                {
                    _stateLock.Release();
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);


            _logger.LogDebug("Requesting TTS stream...");
            var audioSegments = _ttsSynthesizer.SynthesizeStreamingAsync(firstTokenDetectedStream, cancellationToken: cancellationToken);

            // Use the _transcriptionStartTimestamp captured at the beginning of the utterance
            var latencyTrackedAudio = WrapAudioSegments(audioSegments, _transcriptionStartTimestamp, cancellationToken);

            _logger.LogDebug("Starting audio playback...");
            await AudioPlayer.StartPlaybackAsync(latencyTrackedAudio, cancellationToken);

            // Wait for state transition to complete if first token arrived, otherwise cancellation handles it
            // Use a timeout? Rely on cancellation for now.
            await stateTransitionTask.WaitAsync(cancellationToken); // Wait on the specific task for this call

            _logger.LogInformation("Audio playback completed naturally.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("ProcessLlmRequestAsync cancelled.");
            currentFirstTokenTcs.TrySetCanceled(cancellationToken); // Cancel the TCS for this call
            throw; // Re-throw cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during LLM processing, TTS, or playback within ProcessLlmRequestAsync.");
            currentFirstTokenTcs.TrySetException(ex); // Set exception on the TCS for this call
            throw; // Re-throw exception
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogDebug("ProcessLlmRequestAsync finished execution. Elapsed: {Elapsed}ms", stopwatch.ElapsedMilliseconds);
             // Reset the main TCS *only if* it's the one we used and it's completed.
             // This prevents resetting if a new call started quickly.
            if (_firstTokenTcs == currentFirstTokenTcs && _firstTokenTcs.Task.IsCompleted)
            {
                 _firstTokenTcs = null;
            }
        }
    }

    // Modified to accept the specific TCS instance for the current call
    private (IAsyncEnumerable<string> Stream, Task FirstTokenTask) WrapWithFirstTokenDetection(
        IAsyncEnumerable<string> source, Stopwatch stopwatch, TaskCompletionSource<bool> tcs, CancellationToken cancellationToken)
    {
        async IAsyncEnumerable<string> WrappedStream([EnumeratorCancellation] CancellationToken ct = default)
        {
            var firstChunkProcessed = false;
            await foreach (var chunk in source.WithCancellation(ct))
            {
                ct.ThrowIfCancellationRequested(); // Honor cancellation token passed to iterator
                if (!firstChunkProcessed && !string.IsNullOrEmpty(chunk))
                {
                    // TrySetResult is idempotent, safe to call multiple times
                    if (tcs.TrySetResult(true))
                    {
                        // Only log latency the first time TrySetResult succeeds
                        stopwatch.Stop(); // Stop stopwatch on first actual token
                        _logger.LogInformation("First token latency: {Latency}ms", stopwatch.ElapsedMilliseconds);
                    }
                    firstChunkProcessed = true;
                }
                yield return chunk;
            }

            // If the stream completes without yielding anything, try to cancel the TCS
            if (!firstChunkProcessed)
            {
                 _logger.LogWarning("LLM stream completed without yielding any non-empty chunks.");
                 // Use the method's cancellationToken or create a new cancelled one
                 tcs.TrySetCanceled(cancellationToken.IsCancellationRequested ? cancellationToken : new CancellationToken(true));
            }
        }

        return (WrappedStream(cancellationToken), tcs.Task); // Pass cancellationToken to the async iterator
    }


    // Modified to accept the start timestamp
    private async IAsyncEnumerable<AudioSegment> WrapAudioSegments(
        IAsyncEnumerable<AudioSegment> source, DateTimeOffset? utteranceStartTime, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var firstSegment = true;
        await foreach (var segment in source.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (firstSegment)
            {
                firstSegment = false;
                if (utteranceStartTime.HasValue)
                {
                    var latency = DateTimeOffset.Now - utteranceStartTime.Value;
                    _logger.LogInformation("End-to-end latency (to first audio segment): {Latency}ms", latency.TotalMilliseconds);
                }
                else
                {
                     _logger.LogWarning("Could not calculate end-to-end latency, utterance start time not available.");
                }
            }
            yield return segment;
        }
    }

    // --- TriggerBargeInAsync, CancelLlmProcessingAsync, TransitionToStateAsync ---
    // --- No changes needed here for Step 1 ---
    private async Task TriggerBargeInAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("TriggerBargeInAsync entered.");
        await _stateLock.WaitAsync(cancellationToken); // Use passed token or None? Passed token is fine.
        try
        {
            // Check state *before* acting
            if (_state != ConversationState.Streaming)
            {
                _logger.LogWarning("Barge-in trigger attempted but state was {State}. Aborting.", _state);
                return;
            }

            _logger.LogInformation("Triggering Barge-In: Transitioning to Cancelling, stopping audio, cancelling LLM.");
            await TransitionToStateAsync(ConversationState.Cancelling); // Set state first

            // Perform actions concurrently
            var stopAudioTask = AudioPlayer.StopPlaybackAsync();
            var cancelLlmTask = CancelLlmProcessingAsync(); // This already handles internal logic

            // Wait for both actions to complete (or timeout/cancel)
            await Task.WhenAll(stopAudioTask, cancelLlmTask).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken); // Add timeout/cancellation
        }
        catch (OperationCanceledException)
        {
             _logger.LogWarning("Operation cancelled during TriggerBargeInAsync.");
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Timeout during TriggerBargeInAsync while stopping audio/cancelling LLM.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during barge-in trigger execution.");
            // Attempt to recover state? Maybe TransitionToStateAsync(Idle) here?
        }
        finally
        {
            // Ensure state is not left in Cancelling if things went wrong,
            // but HandleProcessingTaskCompletion should eventually move it to Idle/Processing.
            // So, just release the lock.
            _stateLock.Release();
        }
        _logger.LogDebug("TriggerBargeInAsync finished.");
    }


    private async Task CancelLlmProcessingAsync(bool stopAudio = false) // stopAudio flag less critical now, TriggerBargeIn handles it
    {
        _logger.LogInformation("Attempting to cancel current LLM processing... StopAudio={StopAudio}", stopAudio);
        // Capture the specific CTS and Task associated with the *current* active processing
        CancellationTokenSource? ctsToCancel;
        Task? taskToWait;

        // No lock needed here to read the references, but be aware they might change immediately after.
        // The check inside HandleProcessingTaskCompletion prevents stale continuations.
        ctsToCancel = _activeLlmCts;
        taskToWait = _activeProcessingTask;


        if (ctsToCancel != null && !ctsToCancel.IsCancellationRequested)
        {
            _logger.LogDebug("Signalling cancellation via CancellationTokenSource (Instance: {HashCode}).", ctsToCancel.GetHashCode());
            try
            {
                 ctsToCancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
                _logger.LogWarning("Attempted to cancel an already disposed CancellationTokenSource.");
                ctsToCancel = null; // Ensure we don't try to wait on a task associated with a disposed CTS
                taskToWait = null;
            }
        }
        else
        {
            _logger.LogDebug("Cancellation already requested or no active CTS to cancel.");
        }

        // Explicit audio stop if requested (e.g., for immediate silence not triggered by barge-in)
        if (stopAudio)
        {
            _logger.LogDebug("Explicitly stopping audio playback due to cancellation request.");
            try
            {
                // Don't wait indefinitely here
                await AudioPlayer.StopPlaybackAsync().WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during explicit audio stop on cancellation.");
            }
        }

        // Brief wait for the task to potentially observe cancellation.
        // Only wait if we actually cancelled a CTS and have a corresponding task.
        if (taskToWait != null && ctsToCancel != null && !taskToWait.IsCompleted)
        {
            _logger.LogDebug("Waiting briefly for active processing task ({TaskId}) to observe cancellation...", taskToWait.Id);
            // Use Task.WhenAny for a non-blocking wait with timeout
            await Task.WhenAny(taskToWait, Task.Delay(150)); // Increased delay slightly
            _logger.LogDebug("Brief wait completed. Task Status: {Status}", taskToWait.Status);
        }
        else
        {
             _logger.LogDebug("No active/incomplete task found to wait for, or no CTS was cancelled/valid.");
        }
    }


    private Task TransitionToStateAsync(ConversationState newState)
    {
        // No lock needed here if only changing _state and _isInStreamingState,
        // but keep lock for consistency as other methods expect it.
        // await _stateLock.WaitAsync(); // Assuming lock is already held or not needed for simple state change
        // try
        // {
            var oldState = _state;
            if (oldState == newState)
            {
                return Task.CompletedTask;
            }

            _logger.LogInformation("State transition: {OldState} -> {NewState}", oldState, newState);
            _state = newState;

            // Update streaming flag based on new state
            var wasStreaming = _isInStreamingState;
            _isInStreamingState = newState == ConversationState.Streaming;

            // Reset barge-in tracking when exiting streaming state
            if (wasStreaming && !_isInStreamingState)
            {
                _logger.LogDebug("Exiting Streaming state, resetting barge-in tracking.");
                _potentialBargeInStartTime = null;
                _potentialBargeInText.Clear();
            }
        // }
        // finally
        // {
        //     _stateLock.Release();
        // }
        return Task.CompletedTask; // This method itself is synchronous
    }

    // --- END Helper methods ---

    // --- Enums and Records (Keep as is) ---
    private enum ConversationState
    {
        Idle,
        Processing,
        Streaming,
        Cancelling
    }

    // Remove internal record, use Contracts.FinalTranscriptSegmentReceived instead
    // private readonly record struct TranscriptionSegment(string Text, string User, DateTimeOffset Timestamp);
    // --- END Enums and Records ---
}