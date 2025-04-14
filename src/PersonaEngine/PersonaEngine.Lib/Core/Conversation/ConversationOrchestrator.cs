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
    private readonly IAudioOutputService _audioOutputService;

    private readonly IChannelRegistry _channelRegistry;

    private readonly string _llmContext = "Relaxed discussion in discord voice chat.";

    private readonly ILlmProcessor _llmProcessor;

    private readonly Lock _lock = new();

    private readonly ILogger<ConversationOrchestrator> _logger;

    private readonly List<string> _topics = ["casual conversation"];

    private readonly IVisualQAService _visualQaService;

    private CancellationTokenSource? _activeProcessingCts = null;

    private Task? _activeProcessingTask = null;

    private State _currentState = State.Idle;

    public ConversationOrchestrator(
        IChannelRegistry                  channelRegistry,
        ILlmProcessor                     llmProcessor,
        IAudioOutputService               audioOutputService,
        IVisualQAService                  visualQaService,
        ILogger<ConversationOrchestrator> logger)
    {
        _channelRegistry    = channelRegistry;
        _llmProcessor       = llmProcessor;
        _audioOutputService = audioOutputService;
        _visualQaService    = visualQaService;
        _logger             = logger;
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
                    _logger.LogDebug("Orchestrator noted AssistantSpeakingStarted.");
                    if ( _currentState == State.Processing )
                    {
                        TransitionTo(State.Streaming);
                    }
                    else
                    {
                        _logger.LogWarning("AssistantSpeakingStarted received but state was {State}.", _currentState);
                    }

                    break;

                case AssistantSpeakingStopped stopped:
                    _logger.LogDebug("Orchestrator noted AssistantSpeakingStopped. Reason: {Reason}", stopped.Reason);
                    if ( _currentState is State.Streaming or State.Cancelling )
                    {
                        if ( stopped.Reason is AssistantStopReason.Cancelled or AssistantStopReason.Error )
                        {
                            ClearActiveTask("Assistant stopped due to cancellation/error");
                        }

                        TransitionTo(State.Idle);
                    }
                    else
                    {
                        _logger.LogWarning("AssistantSpeakingStopped received but state was {State}.", _currentState);
                    }

                    break;
            }
        }

        return Task.CompletedTask;
    }

    private async Task ProcessUtteranceFlowAsync(UserUtteranceCompleted utterance)
    {
        CancellationTokenSource? currentCts = null;

        lock (_lock) // Assign CTS under lock
        {
            // Ensure we are still in Processing state
            if ( _currentState != State.Processing )
            {
                _logger.LogWarning("ProcessUtteranceFlowAsync started but state changed to {State}. Aborting.", _currentState);

                return;
            }

            _activeProcessingCts?.Dispose(); // Dispose previous if any (shouldn't happen ideally)
            _activeProcessingCts  = new CancellationTokenSource();
            currentCts            = _activeProcessingCts; // Capture the current CTS
            _activeProcessingTask = Task.CompletedTask;   // Placeholder, actual task assigned below
        }

        _logger.LogInformation("Starting LLM processing flow for utterance from {User}.", utterance.User);
        var                       cancellationToken = currentCts.Token;
        IAsyncEnumerable<string>? llmStream         = null;

        try
        {
            // 1. Get LLM Response Stream
            var message = new ChatMessage(utterance.User, utterance.AggregatedText);
            var metadata = new InjectionMetadata(
                                                 _topics.AsReadOnly(),
                                                 _llmContext,
                                                 _visualQaService.ScreenCaption ?? string.Empty); // Get latest caption

            // --- Call LLM Processor ---
            llmStream = _llmProcessor.GetStreamingChatResponseAsync(message, metadata, cancellationToken);

            if ( llmStream == null )
            {
                _logger.LogWarning("LLM Processor returned null stream.");

                throw new InvalidOperationException("LLM processing failed to return a stream.");
            }

            // --- Assign task under lock ---
            // The task represents the *rest* of the flow (audio playback)
            Task audioTask;
            lock (_lock)
            {
                if ( cancellationToken.IsCancellationRequested )
                {
                    throw new OperationCanceledException(); // Check before starting audio
                }

                // Create the task for audio playback but don't await yet
                audioTask             = _audioOutputService.PlayTextStreamAsync(llmStream, utterance.StartTimestamp, cancellationToken);
                _activeProcessingTask = audioTask; // Track the audio task
            }

            // 2. Await Audio Playback (outside lock)
            _logger.LogDebug("Awaiting audio playback task...");
            await audioTask; // Await the task returned and tracked

            // If we reach here without exceptions or cancellation, it completed naturally
            var flowCompletedSuccessfully = !cancellationToken.IsCancellationRequested;
            _logger.LogInformation("LLM/Audio processing flow completed. Success: {Success}", flowCompletedSuccessfully);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("LLM/Audio processing flow was cancelled.");
            // State transition handled by AssistantSpeakingStopped(Cancelled) or direct cancellation logic
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during LLM/Audio processing flow.");
            // State transition handled by AssistantSpeakingStopped(Error)
        }
        finally
        {
            lock (_lock)
            {
                // Only clear/transition if this specific flow's CTS is still the active one
                if ( _activeProcessingCts == currentCts )
                {
                    ClearActiveTask("Flow finally block");

                    // If state wasn't already set back to Idle by AssistantSpeakingStopped, do it now.
                    // This handles cases where LLM fails before audio starts.
                    if ( _currentState == State.Processing || _currentState == State.Streaming || _currentState == State.Cancelling )
                    {
                        _logger.LogDebug("Transitioning to Idle in flow finally block.");
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

    // --- State Management & Cancellation ---

    private void TransitionTo(State newState)
    {
        // Assumes lock is held
        if ( _currentState == newState )
        {
            return;
        }

        _logger.LogInformation("Orchestrator state transition: {OldState} -> {NewState}", _currentState, newState);
        _currentState = newState;

        // Reset things when becoming Idle
        if ( newState == State.Idle )
        {
            ClearActiveTask("Transitioning to Idle");
        }
    }

    private async Task CancelCurrentActivityAsync(string reason)
    {
        _logger.LogInformation("Attempting to cancel current activity. Reason: {Reason}", reason);
        CancellationTokenSource? ctsToCancel   = null;
        Task?                    audioStopTask = null;

        lock (_lock)
        {
            if ( _currentState == State.Processing || _currentState == State.Streaming )
            {
                TransitionTo(State.Cancelling); // Move to cancelling state

                ctsToCancel = _activeProcessingCts; // Get the CTS to cancel

                // Initiate audio stop if we were streaming (or potentially processing if audio could start soon)
                _logger.LogDebug("Initiating audio stop playback.");
                audioStopTask = _audioOutputService.StopPlaybackAsync(); // Fire and forget stop
            }
            else
            {
                _logger.LogWarning("Request to cancel activity, but state is {State}. Nothing to cancel.", _currentState);

                return; // Nothing to do
            }
        }

        // Perform cancellation outside the lock
        if ( ctsToCancel != null )
        {
            _logger.LogDebug("Signalling cancellation token.");
            try
            {
                ctsToCancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
                /* Ignore if already disposed */
            }
            // Don't dispose CTS here, finally block in ProcessUtteranceFlowAsync handles it
        }

        // Await the audio stop task if it was started
        try
        {
            _logger.LogDebug("Awaiting audio stop task...");
            // Add a timeout to prevent hanging
            await audioStopTask.WaitAsync(TimeSpan.FromSeconds(2));
            _logger.LogDebug("Audio stop task completed.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error or timeout awaiting audio stop task during cancellation.");
        }

        // State should eventually transition to Idle via AssistantSpeakingStopped(Cancelled/Error)
        // or the finally block of ProcessUtteranceFlowAsync.
        // We could force transition to Idle here, but relying on the events might be cleaner.
        // Let's add a failsafe: if still Cancelling after a short delay, force Idle.
        _ = Task.Delay(500).ContinueWith(_ =>
                                         {
                                             lock (_lock)
                                             {
                                                 if ( _currentState != State.Cancelling )
                                                 {
                                                     return;
                                                 }

                                                 _logger.LogWarning("Forcing state to Idle after cancellation timeout.");
                                                 TransitionTo(State.Idle);
                                             }
                                         });
    }

    private void ClearActiveTask(string reason)
    {
        // Assumes lock is held
        if ( _activeProcessingTask != null || _activeProcessingCts != null )
        {
            _logger.LogDebug("Clearing active processing task/CTS. Reason: {Reason}", reason);

            _activeProcessingCts?.Dispose();
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