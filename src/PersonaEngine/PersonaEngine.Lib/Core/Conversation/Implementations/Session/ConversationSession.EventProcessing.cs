using System.Threading.Channels;

using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Input;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Session;

public partial class ConversationSession
{
    private async Task ProcessInputEventsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("{SessionId} | Starting input event processing loop.", SessionId);

        try
        {
            await foreach (
                var inputEvent in _inputChannel
                    .Reader.ReadAllAsync(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    if (inputEvent is SttSegmentRecognizing && !_metricsTracker.HasSttStart)
                    {
                        _metricsTracker.RecordSttStart(inputEvent.Timestamp);
                    }

                    if (inputEvent is SttSegmentRecognized sttRecognized)
                    {
                        var sttStart = _metricsTracker.GetSttStartAndClear();
                        if (sttStart.HasValue)
                        {
                            var sttDuration = (
                                sttRecognized.Timestamp - sttStart.Value
                            ).TotalMilliseconds;
                            _metrics.RecordSttSegmentDuration(
                                sttDuration,
                                SessionId,
                                sttRecognized.ParticipantId
                            );
                        }
                    }

                    var (trigger, eventArgs) = MapInputEventToTrigger(inputEvent);

                    if (trigger.HasValue)
                    {
                        _logger.LogTrace(
                            "{SessionId} | Input Loop: Firing trigger {Trigger} for event {EventType}",
                            SessionId,
                            trigger.Value,
                            inputEvent.GetType().Name
                        );

                        await _stateMachine.FireAsync(trigger.Value, eventArgs);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "{SessionId} | Input Loop: Received unhandled input event type: {EventType}",
                            SessionId,
                            inputEvent.GetType().Name
                        );
                    }
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "{SessionId} | Input Loop: State machine transition denied in state {State}. Trigger: {Trigger}, Event: {@Event}",
                        SessionId,
                        _stateMachine.State,
                        MapInputEventToTrigger(inputEvent).Trigger,
                        inputEvent
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "{SessionId} | Input Loop: Error processing input event: {EventType}. Firing ErrorOccurred trigger.",
                        SessionId,
                        inputEvent.GetType().Name
                    );
                    if (
                        _stateMachine.State is ConversationState.Error or ConversationState.Ended
                    )
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("{SessionId} | Input event processing loop cancelled.", SessionId);
        }
        catch (ChannelClosedException)
        {
            _logger.LogInformation(
                "{SessionId} | Input channel closed, ending event processing loop.",
                SessionId
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "{SessionId} | Unhandled exception in input event processing loop.",
                SessionId
            );
            await FireErrorAsync(ex);
        }
        finally
        {
            _logger.LogInformation(
                "{SessionId} | Input event processing loop finished. Final State: {State}",
                SessionId,
                _stateMachine.State
            );
            _inputChannelWriter.TryComplete();
        }
    }

    private async Task ProcessOutputEventsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("{SessionId} | Starting output event processing loop.", SessionId);

        try
        {
            await foreach (
                var outputEvent in _outputChannel
                    .Reader.ReadAllAsync(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    switch (outputEvent)
                    {
                        case LlmStreamStartEvent:
                            _metricsTracker.StartLlmStopwatch();

                            break;
                        case LlmStreamEndEvent ev:
                        {
                            if (!_pipeline.IsAudioOutput)
                            {
                                _metricsTracker.LlmFinishReason = ev.FinishReason;
                                var llmDuration = _metricsTracker.StopLlmStopwatch();
                                if (llmDuration.HasValue)
                                {
                                    _metrics.RecordLlmDuration(
                                        llmDuration.Value,
                                        SessionId,
                                        ev.TurnId ?? Guid.Empty,
                                        _metricsTracker.LlmFinishReason
                                    );
                                }
                            }

                            break;
                        }
                        case TtsReadyToSynthesizeEvent ev:
                        {
                            var latency = _metricsTracker.RecordFirstLlmToken();
                            if (latency.HasValue && ev.TurnId.HasValue)
                            {
                                _logger.LogDebug(
                                    "{SessionId} | {TurnId} | ⏱️ | First LLM [{LatencyMs:N0} ms]",
                                    SessionId,
                                    ev.TurnId.Value,
                                    latency.Value
                                );
                            }

                            break;
                        }
                        case TtsStreamStartEvent:
                            _metricsTracker.StartTtsStopwatch();

                            break;
                        case TtsStreamEndEvent ev:
                        {
                            _metricsTracker.TtsFinishReason = ev.FinishReason;
                            var ttsDuration = _metricsTracker.StopTtsStopwatch();
                            if (ttsDuration.HasValue)
                            {
                                _metrics.RecordTtsDuration(
                                    ttsDuration.Value,
                                    SessionId,
                                    ev.TurnId ?? Guid.Empty,
                                    _metricsTracker.TtsFinishReason
                                );
                            }

                            break;
                        }
                        case AudioPlaybackStartedEvent ev:
                        {
                            var audioLatency = _metricsTracker.RecordFirstAudioChunk();
                            if (audioLatency.HasValue && ev.TurnId.HasValue)
                            {
                                _metrics.RecordFirstAudioLatency(
                                    audioLatency.Value,
                                    SessionId,
                                    ev.TurnId.Value
                                );
                            }

                            _metricsTracker.StartAudioPlaybackStopwatch();

                            break;
                        }
                        case AudioPlaybackEndedEvent ev:
                        {
                            _metricsTracker.AudioFinishReason = ev.FinishReason;
                            var audioDuration = _metricsTracker.StopAudioPlaybackStopwatch();
                            if (audioDuration.HasValue)
                            {
                                _metrics.RecordAudioPlaybackDuration(
                                    audioDuration.Value,
                                    SessionId,
                                    ev.TurnId ?? Guid.Empty,
                                    _metricsTracker.AudioFinishReason
                                );
                            }

                            if (ev.TurnId.HasValue)
                            {
                                var turnDuration = _metricsTracker.StopTurnStopwatch();
                                if (turnDuration.HasValue)
                                {
                                    var overallReason = _metricsTracker.AudioFinishReason;
                                    if (
                                        _metricsTracker.TtsFinishReason
                                            == CompletionReason.Error
                                        || _metricsTracker.LlmFinishReason
                                            == CompletionReason.Error
                                    )
                                    {
                                        overallReason = CompletionReason.Error;
                                    }
                                    else if (
                                        _metricsTracker.TtsFinishReason
                                            == CompletionReason.Cancelled
                                        || _metricsTracker.LlmFinishReason
                                            == CompletionReason.Cancelled
                                    )
                                    {
                                        overallReason = CompletionReason.Cancelled;
                                    }

                                    _metrics.RecordTurnDuration(
                                        turnDuration.Value,
                                        SessionId,
                                        ev.TurnId.Value,
                                        overallReason
                                    );
                                }
                            }

                            break;
                        }
                        case ErrorOutputEvent ev:
                            _metrics.IncrementErrors(SessionId, ev.TurnId, ev.Exception);

                            break;
                    }

                    var (trigger, eventArgs) = MapOutputEventToTrigger(outputEvent);

                    if (trigger.HasValue)
                    {
                        _logger.LogTrace(
                            "{SessionId} | Output Loop: Firing trigger {Trigger} for event {EventType}",
                            SessionId,
                            trigger.Value,
                            outputEvent.GetType().Name
                        );

                        await _stateMachine.FireAsync(trigger.Value, eventArgs);
                    }
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "{SessionId} | Output Loop: State machine transition denied in state {State}. Trigger: {Trigger}, Event: {@Event}",
                        SessionId,
                        _stateMachine.State,
                        MapOutputEventToTrigger(outputEvent).Trigger,
                        outputEvent
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "{SessionId} | Output Loop: Error processing output event: {EventType}. Firing ErrorOccurred trigger.",
                        SessionId,
                        outputEvent.GetType().Name
                    );
                    if (
                        _stateMachine.State is ConversationState.Error or ConversationState.Ended
                    )
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("{SessionId} | Output event processing loop cancelled.", SessionId);
        }
        catch (ChannelClosedException)
        {
            _logger.LogDebug(
                "{SessionId} | Output channel closed, ending output event processing loop.",
                SessionId
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "{SessionId} | Unhandled exception in output event processing loop.",
                SessionId
            );
            await FireErrorAsync(ex);
        }
        finally
        {
            _logger.LogInformation(
                "{SessionId} | Output event processing loop finished. Final State: {State}",
                SessionId,
                _stateMachine.State
            );
            _outputChannelWriter.TryComplete();
        }
    }

    private static (ConversationTrigger? Trigger, object? EventArgs) MapInputEventToTrigger(
        IInputEvent inputEvent
    )
    {
        return inputEvent switch
        {
            SttSegmentRecognizing ev => (ConversationTrigger.InputDetected, ev),
            SttSegmentRecognized ev => (ConversationTrigger.InputFinalized, ev),
            _ => (null, null),
        };
    }

    private static (ConversationTrigger? Trigger, object? EventArgs) MapOutputEventToTrigger(
        IOutputEvent outputEvent
    )
    {
        return outputEvent switch
        {
            LlmStreamStartEvent ev => (ConversationTrigger.LlmStreamStarted, ev),
            LlmChunkEvent ev => (ConversationTrigger.LlmStreamChunkReceived, ev),
            LlmStreamEndEvent ev => (ConversationTrigger.LlmStreamEnded, ev),
            TtsStreamStartEvent ev => (ConversationTrigger.TtsStreamStarted, ev),
            TtsChunkEvent ev => (ConversationTrigger.TtsStreamChunkReceived, ev),
            TtsStreamEndEvent ev => (ConversationTrigger.TtsStreamEnded, ev),
            AudioPlaybackStartedEvent ev => (ConversationTrigger.AudioStreamStarted, ev),
            AudioPlaybackEndedEvent ev => (ConversationTrigger.AudioStreamEnded, ev),
            ErrorOutputEvent ev => (ConversationTrigger.ErrorOccurred, ev.Exception),
            _ => (null, null),
        };
    }
}
