using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using OpenAI.Chat;

using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.Core.Conversation.Implementations.Context;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Input;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;
using PersonaEngine.Lib.LLM;
using PersonaEngine.Lib.TTS.Synthesis;

using Stateless;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Session;

public partial class ConversationSession : IConversationSession
{
    private static readonly ParticipantInfo ASSISTANT_PARTICIPANT = new("ARIA_ASSISTANT_BOT", "Aria", ChatMessageRole.Assistant);

    public ConversationSession(ILogger logger, Guid sessionId, ConversationOptions options, IChatEngine chatEngine, ITtsEngine ttsEngine, IEnumerable<IInputAdapter> inputAdapters, IOutputAdapter outputAdapter)
    {
        _logger     = logger;
        SessionId   = sessionId;
        _options    = options;
        _chatEngine = chatEngine;
        _ttsEngine  = ttsEngine;

        _inputAdapters.AddRange(inputAdapters);
        _outputAdapter = outputAdapter;

        _inputChannel       = Channel.CreateUnbounded<IInputEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        _inputChannelWriter = _inputChannel.Writer;

        _outputChannel       = Channel.CreateUnbounded<IOutputEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        _outputChannelWriter = _outputChannel.Writer;

        _stateMachine = new StateMachine<ConversationState, ConversationTrigger>(ConversationState.Initial);
        ConfigureStateMachine();
    }

    public async ValueTask DisposeAsync()
    {
        if ( _isDisposed )
        {
            return;
        }

        _logger.LogDebug("{SessionId} | Disposing session.", SessionId);

        if ( !_sessionCts.IsCancellationRequested )
        {
            await _sessionCts.CancelAsync();
        }

        if ( _sessionLoopTask is { IsCompleted: false } )
        {
            try
            {
                await _sessionLoopTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("{SessionId} | Timeout waiting for session loop task during dispose.", SessionId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "{SessionId} | Error waiting for session loop task during dispose.", SessionId);
            }
        }

        await CleanupSessionAsync();

        _currentTurnCts?.Dispose();
        _sessionCts.Dispose();

        _inputChannelWriter.TryComplete();
        _outputChannelWriter.TryComplete();

        _isDisposed = true;

        GC.SuppressFinalize(this);
        _logger.LogInformation("{SessionId} | Session disposed.", SessionId);
    }

    public Guid SessionId { get; }

    public async ValueTask RunAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if ( _sessionLoopTask is { IsCompleted: false } )
        {
            _logger.LogWarning("{SessionId} | RunAsync called while session is already running.", SessionId);
            await _sessionLoopTask;

            return;
        }

        using var linkedCts   = CancellationTokenSource.CreateLinkedTokenSource(_sessionCts.Token, cancellationToken);
        var       linkedToken = linkedCts.Token;

        var inputLoopTask  = ProcessInputEventsAsync(linkedToken);
        var outputLoopTask = ProcessOutputEventsAsync(linkedToken);

        _sessionLoopTask = Task.WhenAll(inputLoopTask, outputLoopTask);

        try
        {
            await _stateMachine.FireAsync(ConversationTrigger.InitializeRequested);
            await _sessionLoopTask;

            _logger.LogInformation("{SessionId} | Session run completed. Final State: {State}", SessionId, _stateMachine.State);
        }
        catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
        {
            _logger.LogInformation("{SessionId} | RunAsync cancelled externally or by StopAsync.", SessionId);

            await EnsureStateMachineStoppedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "{SessionId} | Unhandled exception during RunAsync setup or state machine trigger.", SessionId);
            await FireErrorAsync(ex);
        }
        finally
        {
            _logger.LogDebug("{SessionId} | RunAsync finished execution.", SessionId);
            await CleanupSessionAsync();

            _inputChannelWriter.TryComplete();
            _outputChannelWriter.TryComplete();
            _sessionLoopTask = null;
        }
    }

    public async ValueTask StopAsync()
    {
        _logger.LogDebug("{SessionId} | StopAsync called. Current State: {State}", SessionId, _stateMachine.State);

        if ( !_sessionCts.IsCancellationRequested )
        {
            await _sessionCts.CancelAsync();
        }
    }

    #region Event Processing Loops

    private async Task ProcessInputEventsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("{SessionId} | Starting input event processing loop.", SessionId);

        try
        {
            await foreach ( var inputEvent in _inputChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false) )
            {
                if ( cancellationToken.IsCancellationRequested )
                {
                    break;
                }

                try
                {
                    var (trigger, eventArgs) = MapInputEventToTrigger(inputEvent);

                    if ( trigger.HasValue )
                    {
                        _logger.LogTrace("{SessionId} | Input Loop: Firing trigger {Trigger} for event {EventType}", SessionId, trigger.Value, inputEvent.GetType().Name);

                        await _stateMachine.FireAsync(trigger.Value, eventArgs);
                    }
                    else
                    {
                        _logger.LogWarning("{SessionId} | Input Loop: Received unhandled input event type: {EventType}", SessionId, inputEvent.GetType().Name);
                    }
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "{SessionId} | Input Loop: State machine transition denied in state {State}. Trigger: {Trigger}, Event: {@Event}",
                                       SessionId, _stateMachine.State, MapInputEventToTrigger(inputEvent).Trigger, inputEvent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{SessionId} | Input Loop: Error processing input event: {EventType}. Firing ErrorOccurred trigger.", SessionId, inputEvent.GetType().Name);
                    // await FireErrorWithoutStoppingAsync(ex);
                    if ( _stateMachine.State is ConversationState.Error or ConversationState.Ended )
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("{SessionId} | Input event processing loop cancelled.", SessionId);
        }
        catch (ChannelClosedException)
        {
            _logger.LogInformation("{SessionId} | Input channel closed, ending event processing loop.", SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{SessionId} | Unhandled exception in input event processing loop.", SessionId);
            await FireErrorAsync(ex);
        }
        finally
        {
            _logger.LogInformation("{SessionId} | Input event processing loop finished. Final State: {State}", SessionId, _stateMachine.State);
            _inputChannelWriter.TryComplete();
        }
    }

    private async Task ProcessOutputEventsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("{SessionId} | Starting output event processing loop.", SessionId);

        try
        {
            await foreach ( var outputEvent in _outputChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false) )
            {
                if ( cancellationToken.IsCancellationRequested )
                {
                    break;
                }

                try
                {
                    var (trigger, eventArgs) = MapOutputEventToTrigger(outputEvent);

                    if ( trigger.HasValue )
                    {
                        _logger.LogTrace("{SessionId} | Output Loop: Firing trigger {Trigger} for event {EventType}", SessionId, trigger.Value, outputEvent.GetType().Name);

                        await _stateMachine.FireAsync(trigger.Value, eventArgs);
                    }
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "{SessionId} | Output Loop: State machine transition denied in state {State}. Trigger: {Trigger}, Event: {@Event}",
                                       SessionId, _stateMachine.State, MapOutputEventToTrigger(outputEvent).Trigger, outputEvent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{SessionId} | Output Loop: Error processing output event: {EventType}. Firing ErrorOccurred trigger.", SessionId, outputEvent.GetType().Name);
                    // await FireErrorWithoutStoppingAsync(ex);
                    if ( _stateMachine.State is ConversationState.Error or ConversationState.Ended )
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("{SessionId} | Output event processing loop cancelled.", SessionId);
        }
        catch (ChannelClosedException)
        {
            _logger.LogInformation("{SessionId} | Output channel closed, ending output event processing loop.", SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{SessionId} | Unhandled exception in output event processing loop.", SessionId);
            await FireErrorAsync(ex);
        }
        finally
        {
            _logger.LogInformation("{SessionId} | Output event processing loop finished. Final State: {State}", SessionId, _stateMachine.State);
            _inputChannelWriter.TryComplete();
        }
    }

    private (ConversationTrigger? Trigger, object? EventArgs) MapInputEventToTrigger(IInputEvent inputEvent)
    {
        return inputEvent switch {
            SttSegmentRecognizing ev => (ConversationTrigger.InputDetected, ev),
            SttSegmentRecognized ev => (ConversationTrigger.InputFinalized, ev),
            _ => (null, null)
        };
    }

    private (ConversationTrigger? Trigger, object? EventArgs) MapOutputEventToTrigger(IOutputEvent outputEvent)
    {
        return outputEvent switch {
            LlmStreamStartEvent ev => (ConversationTrigger.LlmStreamStarted, ev),
            LlmChunkEvent ev => (ConversationTrigger.LlmStreamChunkReceived, ev),
            LlmStreamEndEvent ev => (ConversationTrigger.LlmStreamEnded, ev),
            TtsStreamStartEvent ev => (ConversationTrigger.TtsStreamStarted, ev),
            TtsChunkEvent ev => (ConversationTrigger.TtsStreamChunkReceived, ev),
            TtsStreamEndEvent ev => (ConversationTrigger.TtsStreamEnded, ev),
            AudioPlaybackStartedEvent ev => (ConversationTrigger.AudioStreamStarted, ev),
            AudioPlaybackEndedEvent ev => (ConversationTrigger.AudioStreamEnded, ev),
            ErrorOutputEvent ev => (ConversationTrigger.ErrorOccurred, ev.Exception),
            _ => (null, null)
        };
    }

    #endregion

    #region Fields

    private Task? _responseGenerationTask;

    private readonly ILogger _logger;

    private readonly ConversationOptions _options;

    private readonly IChatEngine _chatEngine;

    private readonly ITtsEngine _ttsEngine;

    private readonly List<IInputAdapter> _inputAdapters = new();

    private readonly IOutputAdapter _outputAdapter;

    private readonly CancellationTokenSource _sessionCts = new();

    private CancellationTokenSource? _currentTurnCts;

    private Task? _sessionLoopTask;

    private Guid? _currentTurnId;

    private readonly Channel<IInputEvent> _inputChannel;

    private readonly ChannelWriter<IInputEvent> _inputChannelWriter;

    private readonly Channel<IOutputEvent> _outputChannel;

    private readonly ChannelWriter<IOutputEvent> _outputChannelWriter;

    private Channel<LlmChunkEvent>? _currentLlmChannel;

    private Channel<TtsChunkEvent>? _currentTtsChannel;

    private bool _isDisposed = false;

    private ConversationContext? _context;

    #endregion

    # region State Actions

    private async Task InitializeSessionAsync()
    {
        _logger.LogDebug("{SessionId} | Action: InitializeSessionAsync", SessionId);

        try
        {
            var initInputTasks = _inputAdapters.Select(async adapter =>
                                                       {
                                                           await adapter.InitializeAsync(SessionId, _inputChannelWriter, _sessionCts.Token);
                                                           await adapter.StartAsync(_sessionCts.Token);
                                                           _logger.LogDebug("{SessionId} | Input Adapter {AdapterId} initialized and started.", SessionId, adapter.AdapterId);
                                                       }).ToList();

            var initOutputTasks = Task.Run(async () =>
                                           {
                                               await _outputAdapter.InitializeAsync(SessionId, _sessionCts.Token);
                                               await _outputAdapter.StartAsync(_sessionCts.Token);
                                               _logger.LogDebug("{SessionId} | Output Adapter {AdapterId} initialized and started.", SessionId, _outputAdapter.AdapterId);
                                           });

            await Task.WhenAll(initInputTasks.Concat([initOutputTasks]));

            _context = new ConversationContext(_inputAdapters.Select(adapter => adapter.Participant).Concat([ASSISTANT_PARTICIPANT]).ToList());

            await _stateMachine.FireAsync(ConversationTrigger.InitializeComplete);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{SessionId} | Error during InitializeSessionAsync.", SessionId);
            await _stateMachine.FireAsync(ConversationTrigger.ErrorOccurred, ex);
        }
    }

    private async Task PrepareLlmRequestAsync(IInputEvent inputEvent)
    {
        if ( inputEvent is not SttSegmentRecognized finalizedEvent )
        {
            _logger.LogWarning("{SessionId} | PrepareLlmRequestAsync received unexpected event type: {EventType}", SessionId, inputEvent.GetType().Name);
            await FireErrorAsync(new ArgumentException("Invalid event type for InputFinalized trigger.", nameof(inputEvent)), false);

            return;
        }

        if ( _context is null )
        {
            _logger.LogWarning("{SessionId} | Context is null in PrepareLlmRequestAsync.", SessionId);
            await FireErrorAsync(new InvalidOperationException("Context is null."));

            return;
        }

        _logger.LogDebug("{SessionId} | Action: PrepareLlmRequestAsync for input: \"{InputText}\"", SessionId, finalizedEvent.FinalTranscript);

        await CancelResponseGenerationAsync();

        _currentTurnCts = CancellationTokenSource.CreateLinkedTokenSource(_sessionCts.Token);
        _currentTurnId  = Guid.NewGuid();

        try
        {
            _context.StartTurn(_currentTurnId.Value, [finalizedEvent.ParticipantId, ASSISTANT_PARTICIPANT.Id]);
            _context.AppendToTurn(finalizedEvent.ParticipantId, finalizedEvent.FinalTranscript);

            _logger.LogInformation("{SessionId} | {TurnId} | {Emoji} | [{Speaker}]{Text}", SessionId, _currentTurnId, "📝", _context.Participants[finalizedEvent.ParticipantId].Name, _context.PendingChunk(finalizedEvent.ParticipantId));

            await _stateMachine.FireAsync(ConversationTrigger.LlmRequestSent);

            if ( _outputAdapter is IAudioOutputAdapter )
            {
                await _stateMachine.FireAsync(ConversationTrigger.TtsRequestSent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{SessionId} | Error during PrepareLlmRequestAsync for Turn {TurnId}.", SessionId, _currentTurnId ?? Guid.Empty);
            await FireErrorAsync(ex);
        }
    }

    private async Task CancelResponseGenerationAsync()
    {
        if ( _currentTurnCts is { IsCancellationRequested: false } )
        {
            _logger.LogDebug("{SessionId} | Requesting cancellation for Turn {TurnId}'s pipeline.", SessionId, _currentTurnId);
            try
            {
                await _currentTurnCts.CancelAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{SessionId} | Error during CancelAsync for Turn {TurnId}.", SessionId, _currentTurnId);
            }
        }

        if ( _responseGenerationTask is { IsCompleted: false } )
        {
            _logger.LogDebug("{SessionId} | Waiting briefly for response generation task cancellation for Turn {TurnId}.", SessionId, _currentTurnId);
            try
            {
                await Task.WhenAny(_responseGenerationTask, Task.Delay(TimeSpan.FromMilliseconds(500)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "{SessionId} | Exception waiting for response generation task cancellation (Turn {TurnId}).", SessionId, _currentTurnId);
            }
        }

        _currentTurnCts?.Dispose();
        _currentTurnCts         = null;
        _currentTurnId          = null;
        _responseGenerationTask = null;

        _context?.AbortTurn();

        _currentLlmChannel?.Writer.TryComplete();
        _currentTtsChannel?.Writer.TryComplete();
        _currentLlmChannel = null;
        _currentTtsChannel = null;
    }

    private Task HandleInterruptionAsync(IInputEvent inputEvent)
    {
        _context?.CompleteTurn(ASSISTANT_PARTICIPANT.Id, true);
        CommitChanges(true);

        return Task.CompletedTask;
    }

    private async Task PauseActivitiesAsync()
    {
        _logger.LogInformation("{SessionId} | Action: PauseActivitiesAsync - Stopping Adapters", SessionId);

        // TODO: Need to think about this
        foreach ( var adapter in _inputAdapters )
        {
            try
            {
                await adapter.StopAsync(_sessionCts.Token);
            } // Use session token? Or None?
            catch (Exception ex)
            {
                _logger.LogError(ex, "{SessionId} | Error stopping input adapter {AdapterId} during pause.", SessionId, adapter.AdapterId);
            }
        }
    }

    private async Task ResumeActivitiesAsync()
    {
        _logger.LogInformation("{SessionId} | Action: ResumeActivitiesAsync - Starting Adapters", SessionId);

        // TODO: Need to think about this
        foreach ( var adapter in _inputAdapters )
        {
            try
            {
                await adapter.StartAsync(_sessionCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{SessionId} | Error starting input adapter {AdapterId} during resume.", SessionId, adapter.AdapterId);
            }
        }
    }

    private Task HandleErrorAsync(Exception error)
    {
        _logger.LogError(error, "{SessionId} | Action: HandleErrorAsync - An error occurred in the state machine.", SessionId);
        _stateMachine.FireAsync(ConversationTrigger.StopRequested);

        return Task.CompletedTask;
    }

    private async Task CleanupSessionAsync()
    {
        await CancelResponseGenerationAsync();

        foreach ( var adapter in _inputAdapters )
        {
            try
            {
                // Use CancellationToken.None as session might be ending
                await adapter.StopAsync(CancellationToken.None);
                await adapter.DisposeAsync();
                _logger.LogDebug("{SessionId} | Input Adapter {AdapterId} stopped and disposed.", SessionId, adapter.AdapterId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{SessionId} | Error cleaning up input adapter {AdapterId}.", SessionId, adapter.AdapterId);
            }
        }

        _inputAdapters.Clear();

        try
        {
            await _outputAdapter.StopAsync(CancellationToken.None);
            await _outputAdapter.DisposeAsync();
            _logger.LogDebug("{SessionId} | Output Adapter {AdapterId} stopped and disposed.", SessionId, _outputAdapter.AdapterId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{SessionId} | Error cleaning up output adapter {AdapterId}.", SessionId, _outputAdapter.AdapterId);
        }

        _logger.LogInformation("{SessionId} | Cleanup finished.", SessionId);
    }

    private void HandleLlmStreamRequested()
    {
        var turnId       = _currentTurnId;
        var turnCts      = _currentTurnCts;
        var outputWriter = _outputChannelWriter;

        if ( !turnId.HasValue || turnCts is null )
        {
            _logger.LogWarning("{SessionId} | StartLlm called without a valid TurnId.", SessionId);

            return;
        }

        if ( _context is null )
        {
            _logger.LogWarning("{SessionId} | Context is null in HandleLlmStreamRequested.", SessionId);

            return;
        }

        _currentLlmChannel = Channel.CreateUnbounded<LlmChunkEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        _ = _chatEngine.GetStreamingChatResponseAsync(_context, outputWriter, turnId.Value, SessionId, cancellationToken: turnCts.Token);
    }

    private void HandleTtsStreamRequest()
    {
        var turnId              = _currentTurnId;
        var turnCts             = _currentTurnCts;
        var outputChannelWriter = _outputChannelWriter;
        var outputChannelReader = _currentLlmChannel;

        if ( !turnId.HasValue || turnCts is null || outputChannelReader is null )
        {
            _logger.LogWarning("{SessionId} | HandleTtsStreamRequest called without a valid TurnId.", SessionId);

            return;
        }

        _currentTtsChannel = Channel.CreateUnbounded<TtsChunkEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        _ = _ttsEngine.SynthesizeStreamingAsync(outputChannelReader, outputChannelWriter, turnId.Value, SessionId, cancellationToken: turnCts.Token);

        if ( _outputAdapter is IAudioOutputAdapter audioOutput )
        {
            _ = audioOutput.SendAsync(_currentTtsChannel, outputChannelWriter, turnId.Value, turnCts.Token);
        }
    }

    private async Task HandleLlmStreamChunkReceived(IOutputEvent outputEvent, StateMachine<ConversationState, ConversationTrigger>.Transition _)
    {
        if ( outputEvent is not LlmChunkEvent chunkEvent )
        {
            _logger.LogWarning("{SessionId} | HandleLlmStreamChunkReceived received unexpected event type: {EventType}", SessionId, outputEvent.GetType().Name);
            await _stateMachine.FireAsync(ConversationTrigger.ErrorOccurred, new ArgumentException("Invalid event type for LlmStreamChunkReceived trigger.", nameof(outputEvent)));

            return;
        }

        var turnId       = _currentTurnId;
        var turnCts      = _currentTurnCts;
        var outputWriter = _currentLlmChannel?.Writer;

        if ( !turnId.HasValue || turnCts is null || outputWriter is null )
        {
            _logger.LogWarning("{SessionId} | HandleLlmStreamChunkReceived called without a valid TurnId.", SessionId);

            return;
        }

        _context?.AppendToTurn(ASSISTANT_PARTICIPANT.Id, chunkEvent.Chunk);

        await outputWriter.WriteAsync(chunkEvent, turnCts.Token);
    }

    private async Task HandleTtsStreamChunkReceived(IOutputEvent outputEvent, StateMachine<ConversationState, ConversationTrigger>.Transition _)
    {
        if ( outputEvent is not TtsChunkEvent chunkEvent )
        {
            _logger.LogWarning("{SessionId} | HandleTtsStreamChunkReceived received unexpected event type: {EventType}", SessionId, outputEvent.GetType().Name);
            await _stateMachine.FireAsync(ConversationTrigger.ErrorOccurred, new ArgumentException("Invalid event type for HandleTtsStreamChunkReceived trigger.", nameof(outputEvent)));

            return;
        }

        var turnId       = _currentTurnId;
        var turnCts      = _currentTurnCts;
        var outputWriter = _currentTtsChannel?.Writer;

        if ( !turnId.HasValue || turnCts is null || outputWriter is null )
        {
            _logger.LogWarning("{SessionId} | HandleTtsStreamChunkReceived called without a valid TurnId.", SessionId);

            return;
        }

        await outputWriter.WriteAsync(chunkEvent, turnCts.Token);
    }

    private void HandleLlmStreamEnded()
    {
        var outputWriter = _currentLlmChannel?.Writer;

        if ( outputWriter is null )
        {
            _logger.LogWarning("{SessionId} | HandleLlmStreamEnded called without a valid LLM channel.", SessionId);

            return;
        }

        outputWriter.TryComplete();

        _context?.CompleteTurn(ASSISTANT_PARTICIPANT.Id, false);
    }

    private void HandleTtsStreamEnded()
    {
        var outputWriter = _currentTtsChannel?.Writer;

        if ( outputWriter is null )
        {
            _logger.LogWarning("{SessionId} | HandleTtsStreamEnded called without a valid TTS channel.", SessionId);

            return;
        }

        outputWriter.TryComplete();
    }

    private async Task FireErrorAsync(Exception ex, bool stopSession = true)
    {
        try
        {
            if ( _stateMachine.State != ConversationState.Error && _stateMachine.State != ConversationState.Ended )
            {
                await _stateMachine.FireAsync(ConversationTrigger.ErrorOccurred, ex);
            }

            if ( stopSession && _stateMachine.State != ConversationState.Ended )
            {
                await StopAsync();
            }
        }
        catch (Exception fireEx)
        {
            _logger.LogError(fireEx, "{SessionId} | Failed to fire ErrorOccurred/StopRequested after initial exception.", SessionId);
            if ( !_sessionCts.IsCancellationRequested )
            {
                await _sessionCts.CancelAsync();
            }
        }
    }

    private async Task EnsureStateMachineStoppedAsync()
    {
        if ( _stateMachine.State != ConversationState.Ended && _stateMachine.State != ConversationState.Error )
        {
            _logger.LogWarning("{SessionId} | Session loop ended unexpectedly in state {State}. Forcing StopRequested.", SessionId, _stateMachine.State);
            try
            {
                if ( !_sessionCts.IsCancellationRequested )
                {
                    await _sessionCts.CancelAsync();
                }

                await _stateMachine.FireAsync(ConversationTrigger.StopRequested);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{SessionId} | Exception while trying to force StopRequested.", SessionId);
            }
        }
    }

    private void HandleIdle() { CommitChanges(false); }

    private void CommitChanges(bool interrupted)
    {
        foreach ( var inputAdapter in _inputAdapters )
        {
            _context?.CompleteTurn(inputAdapter.Participant.Id, interrupted);
        }
    }

    #endregion
}