using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Input;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;
using Stateless;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Session;

public partial class ConversationSession
{
    private async Task InitializeSessionAsync()
    {
        _logger.LogDebug("{SessionId} | Action: InitializeSessionAsync", SessionId);

        try
        {
            _context.TryAddParticipant(ASSISTANT_PARTICIPANT);

            var initInputTasks = _inputAdapters
                .Select(async adapter =>
                {
                    await adapter.InitializeAsync(
                        SessionId,
                        _inputChannelWriter,
                        _sessionCts.Token
                    );
                    await adapter.StartAsync(_sessionCts.Token);
                    _context.TryAddParticipant(adapter.Participant);

                    _logger.LogDebug(
                        "{SessionId} | Input Adapter {AdapterId} initialized and started.",
                        SessionId,
                        adapter.AdapterId
                    );
                })
                .ToList();

            var initOutputTasks = Task.Run(async () =>
            {
                await _outputAdapter.InitializeAsync(SessionId, _sessionCts.Token);
                await _outputAdapter.StartAsync(_sessionCts.Token);
                _logger.LogDebug(
                    "{SessionId} | Output Adapter {AdapterId} initialized and started.",
                    SessionId,
                    _outputAdapter.AdapterId
                );
            });

            await Task.WhenAll(initInputTasks.Concat([initOutputTasks]));

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
        if (inputEvent is not SttSegmentRecognized finalizedEvent)
        {
            _logger.LogWarning(
                "{SessionId} | PrepareLlmRequestAsync received unexpected event type: {EventType}",
                SessionId,
                inputEvent.GetType().Name
            );
            await FireErrorAsync(
                new ArgumentException(
                    "Invalid event type for InputFinalized trigger.",
                    nameof(inputEvent)
                ),
                false
            );

            return;
        }

        _logger.LogDebug(
            "{SessionId} | Action: PrepareLlmRequestAsync for input: \"{InputText}\"",
            SessionId,
            finalizedEvent.FinalTranscript
        );

        await CancelCurrentTurnProcessingAsync();

        var turnId = _pipeline.StartNewTurn(_sessionCts.Token);
        _metricsTracker.StartTurn(finalizedEvent.ProcessingDuration.TotalMilliseconds);

        _metrics.IncrementTurnsStarted(SessionId);

        try
        {
            _context.StartTurn(turnId, [finalizedEvent.ParticipantId, ASSISTANT_PARTICIPANT.Id]);
            _context.AppendToTurn(finalizedEvent.ParticipantId, finalizedEvent.FinalTranscript);

            var userName = _context.Participants.TryGetValue(
                finalizedEvent.ParticipantId,
                out var pInfo
            )
                ? pInfo.Name
                : finalizedEvent.ParticipantId;
            _logger.LogInformation(
                "{SessionId} | {TurnId} | 📝 | [{Speaker}]{Text}",
                SessionId,
                turnId,
                userName,
                _context.GetPendingMessageText(finalizedEvent.ParticipantId)
            );

            _context.CompleteTurnPart(finalizedEvent.ParticipantId);

            await _stateMachine.FireAsync(ConversationTrigger.LlmRequestSent);

            if (_pipeline.IsAudioOutput)
            {
                await _stateMachine.FireAsync(ConversationTrigger.TtsRequestSent);
            }
            else
            {
                _metricsTracker.ClearNonAudioTtsTracking();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "{SessionId} | Error during PrepareLlmRequestAsync for Turn {TurnId}.",
                SessionId,
                _pipeline.CurrentTurnId ?? Guid.Empty
            );
            await FireErrorAsync(ex);
        }
    }

    private async Task CancelCurrentTurnProcessingAsync()
    {
        await _pipeline.CancelCurrentAsync();
        _context.AbortTurn();
        _metricsTracker.Reset();
    }

    private void HandleInterruption()
    {
        var interruptedTurnId = _pipeline.CurrentTurnId;

        if (!interruptedTurnId.HasValue)
        {
            _logger.LogWarning(
                "{SessionId} | Interruption handled but no active turn ID found.",
                SessionId
            );

            return;
        }

        _metrics.IncrementTurnsInterrupted(SessionId, interruptedTurnId.Value);

        CommitChanges(false);
    }

    private async Task HandleInterruptionCancelAndRefireAsync(
        IInputEvent inputEvent,
        StateMachine<ConversationState, ConversationTrigger>.Transition transition
    )
    {
        HandleInterruption();

        if (transition.Source is ConversationState.Speaking)
        {
            _context.CompleteTurnPart(ASSISTANT_PARTICIPANT.Id, true);
        }

        switch (transition.Trigger)
        {
            case ConversationTrigger.InputDetected:
                await _stateMachine.FireAsync(_inputDetectedTrigger, inputEvent);

                return;
            case ConversationTrigger.InputFinalized:
                await _stateMachine.FireAsync(_inputFinalizedTrigger, inputEvent);

                return;
            default:
                throw new ArgumentException(
                    $"{transition} failed. The state machine is in an invalid state.",
                    nameof(transition)
                );
        }
    }

    private async Task PauseActivitiesAsync()
    {
        _logger.LogInformation(
            "{SessionId} | Action: PauseActivitiesAsync - Stopping Adapters",
            SessionId
        );

        foreach (var adapter in _inputAdapters)
        {
            try
            {
                await adapter.StopAsync(_sessionCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{SessionId} | Error stopping input adapter {AdapterId} during pause.",
                    SessionId,
                    adapter.AdapterId
                );
            }
        }
    }

    private async Task ResumeActivitiesAsync()
    {
        _logger.LogInformation(
            "{SessionId} | Action: ResumeActivitiesAsync - Starting Adapters",
            SessionId
        );

        foreach (var adapter in _inputAdapters)
        {
            try
            {
                await adapter.StartAsync(_sessionCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{SessionId} | Error starting input adapter {AdapterId} during resume.",
                    SessionId,
                    adapter.AdapterId
                );
            }
        }
    }

    private async Task HandleErrorAsync(Exception error)
    {
        _logger.LogError(
            error,
            "{SessionId} | Action: HandleErrorAsync - An error occurred in the state machine.",
            SessionId
        );
        _metrics.IncrementErrors(SessionId, _pipeline.CurrentTurnId, error);
        await _stateMachine.FireAsync(ConversationTrigger.StopRequested);
    }

    private async Task CleanupSessionAsync()
    {
        await CancelCurrentTurnProcessingAsync();

        var inputCleanupTasks = _inputAdapters
            .Select(async adapter =>
            {
                try
                {
                    await adapter.StopAsync(CancellationToken.None);
                    await adapter.DisposeAsync();
                    _logger.LogDebug(
                        "{SessionId} | Input Adapter {AdapterId} stopped and disposed.",
                        SessionId,
                        adapter.AdapterId
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "{SessionId} | Error cleaning up input adapter {AdapterId}.",
                        SessionId,
                        adapter.AdapterId
                    );
                }
            })
            .ToList();

        var outputCleanupTask = Task.Run(async () =>
        {
            try
            {
                await _outputAdapter.StopAsync(CancellationToken.None);
                await _outputAdapter.DisposeAsync();
                _logger.LogDebug(
                    "{SessionId} | Output Adapter {AdapterId} stopped and disposed.",
                    SessionId,
                    _outputAdapter.AdapterId
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{SessionId} | Error cleaning up output adapter {AdapterId}.",
                    SessionId,
                    _outputAdapter.AdapterId
                );
            }
        });

        await Task.WhenAll(inputCleanupTasks.Append(outputCleanupTask));
        _inputAdapters.Clear();
        _logger.LogDebug("{SessionId} | Cleanup finished.", SessionId);
    }

    private void HandleLlmStreamRequested()
    {
        try
        {
            _pipeline.StartLlmStage(_context, _outputChannelWriter, SessionId);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(
                ex,
                "{SessionId} | HandleLlmStreamRequested called in invalid state.",
                SessionId
            );
            _ = FireErrorAsync(ex, false);
        }
    }

    private void HandleTtsStreamRequest()
    {
        _pipeline.StartTtsStage(_outputChannelWriter, SessionId);
    }

    private async Task HandleLlmStreamChunkReceived(
        IOutputEvent outputEvent,
        StateMachine<ConversationState, ConversationTrigger>.Transition _
    )
    {
        if (outputEvent is not LlmChunkEvent chunkEvent)
        {
            _logger.LogWarning(
                "{SessionId} | HandleLlmStreamChunkReceived received unexpected event type: {EventType}",
                SessionId,
                outputEvent.GetType().Name
            );
            await _stateMachine.FireAsync(
                ConversationTrigger.ErrorOccurred,
                new ArgumentException(
                    "Invalid event type for LlmStreamChunkReceived trigger.",
                    nameof(outputEvent)
                )
            );

            return;
        }

        if (!_pipeline.HasValidTurn)
        {
            _logger.LogWarning(
                "{SessionId} | HandleLlmStreamChunkReceived called without a valid TurnId.",
                SessionId
            );

            return;
        }

        if (!_pipeline.IsAudioOutput)
        {
            var latency = _metricsTracker.RecordFirstLlmToken();
            if (latency.HasValue)
            {
                _logger.LogDebug(
                    "{SessionId} | {TurnId} | ⏱️ | First LLM [{LatencyMs:N0} ms]",
                    SessionId,
                    _pipeline.CurrentTurnId!.Value,
                    latency.Value
                );
            }
        }

        _context.AppendToTurn(ASSISTANT_PARTICIPANT.Id, chunkEvent.Chunk);

        await _pipeline.WriteLlmChunkAsync(chunkEvent, _pipeline.TurnCancellationToken!.Value);
    }

    private async Task HandleTtsStreamChunkReceived(
        IOutputEvent outputEvent,
        StateMachine<ConversationState, ConversationTrigger>.Transition _
    )
    {
        if (outputEvent is not TtsChunkEvent chunkEvent)
        {
            _logger.LogWarning(
                "{SessionId} | HandleTtsStreamChunkReceived received unexpected event type: {EventType}",
                SessionId,
                outputEvent.GetType().Name
            );
            await _stateMachine.FireAsync(
                ConversationTrigger.ErrorOccurred,
                new ArgumentException(
                    "Invalid event type for HandleTtsStreamChunkReceived trigger.",
                    nameof(outputEvent)
                )
            );

            return;
        }

        if (!_pipeline.HasValidTurn)
        {
            _logger.LogWarning(
                "{SessionId} | HandleTtsStreamChunkReceived called without a valid TurnId.",
                SessionId
            );

            return;
        }

        var latency = _metricsTracker.RecordFirstTtsChunk();
        if (latency.HasValue)
        {
            _logger.LogDebug(
                "{SessionId} | {TurnId} | ⏱️ | First TTS [{LatencyMs:N0} ms]",
                SessionId,
                _pipeline.CurrentTurnId!.Value,
                latency.Value
            );
        }

        await _pipeline.WriteTtsChunkAsync(chunkEvent, _pipeline.TurnCancellationToken!.Value);
    }

    private void HandleLlmStreamEnded()
    {
        _pipeline.CompleteLlmChannel();

        if (!_pipeline.IsAudioOutput)
        {
            _context.CompleteTurnPart(ASSISTANT_PARTICIPANT.Id);
        }
    }

    private void HandleTtsStreamEnded()
    {
        _pipeline.CompleteTtsChannel();
    }

    private async Task FireErrorAsync(Exception ex, bool stopSession = true)
    {
        try
        {
            if (
                _stateMachine.State != ConversationState.Error
                && _stateMachine.State != ConversationState.Ended
            )
            {
                await _stateMachine.FireAsync(ConversationTrigger.ErrorOccurred, ex);
            }

            if (stopSession && _stateMachine.State != ConversationState.Ended)
            {
                await StopAsync();
            }
        }
        catch (Exception fireEx)
        {
            _logger.LogError(
                fireEx,
                "{SessionId} | Failed to fire ErrorOccurred/StopRequested after initial exception.",
                SessionId
            );
            if (!_sessionCts.IsCancellationRequested)
            {
                await _sessionCts.CancelAsync();
            }
        }
    }

    private async Task EnsureStateMachineStoppedAsync()
    {
        if (
            _stateMachine.State != ConversationState.Ended
            && _stateMachine.State != ConversationState.Error
        )
        {
            _logger.LogWarning(
                "{SessionId} | Session loop ended unexpectedly in state {State}. Forcing StopRequested.",
                SessionId,
                _stateMachine.State
            );
            try
            {
                if (!_sessionCts.IsCancellationRequested)
                {
                    await _sessionCts.CancelAsync();
                }

                await _stateMachine.FireAsync(ConversationTrigger.StopRequested);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{SessionId} | Exception while trying to force StopRequested.",
                    SessionId
                );
            }
        }
    }

    private void CommitSpokenText()
    {
        _context.CompleteTurnPart(ASSISTANT_PARTICIPANT.Id);
    }

    private void HandleIdle()
    {
        var completedTurnId = _pipeline.CurrentTurnId;
        var summary = _metricsTracker.CompleteTurn();

        if (completedTurnId.HasValue && summary.TurnDurationMs.HasValue)
        {
            var sttTime = summary.SttLatencyMs ?? 0;
            var llmTime = summary.FirstLlmTokenMs ?? 0;
            var ttsTime = summary.FirstTtsChunkMs ?? 0;
            var audioTime = summary.FirstAudioMs ?? 0;

            _logger.LogInformation(
                "{SessionId} | {TurnId} | ⏱️ | STT @{SttMs:N0}ms ⇢ LLM @{LlmMs:N0}ms ⇢ TTS @{TtsMs:N0}ms ⇢ AUDIO @{AudioMs:N0}ms | TOTAL @{TotalMs:N0}ms",
                SessionId,
                completedTurnId.Value,
                sttTime,
                llmTime,
                ttsTime,
                audioTime,
                sttTime + llmTime + ttsTime + audioTime
            );
        }

        CommitChanges(false);

        _pipeline.TryCompleteAllChannels();
    }

    private void CommitChanges(bool interrupted)
    {
        foreach (var inputAdapter in _inputAdapters)
        {
            _context.CompleteTurnPart(inputAdapter.Participant.Id, interrupted);
        }
    }
}
