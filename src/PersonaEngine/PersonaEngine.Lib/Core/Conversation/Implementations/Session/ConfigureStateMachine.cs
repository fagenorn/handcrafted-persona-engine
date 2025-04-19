using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Strategies;

using Stateless;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Session;

public partial class ConversationSession
{
    private readonly StateMachine<ConversationState, ConversationTrigger> _stateMachine;

    private StateMachine<ConversationState, ConversationTrigger>.TriggerWithParameters<IInputEvent> _inputDetectedTrigger = null!;

    private StateMachine<ConversationState, ConversationTrigger>.TriggerWithParameters<IInputEvent> _inputFinalizedTrigger = null!;

    private void ConfigureStateMachine()
    {
        _inputDetectedTrigger  = _stateMachine.SetTriggerParameters<IInputEvent>(ConversationTrigger.InputDetected);
        _inputFinalizedTrigger = _stateMachine.SetTriggerParameters<IInputEvent>(ConversationTrigger.InputFinalized);

        var llmChunkReceivedTrigger = _stateMachine.SetTriggerParameters<IOutputEvent>(ConversationTrigger.LlmStreamChunkReceived);
        var ttsChunkReceivedTrigger = _stateMachine.SetTriggerParameters<IOutputEvent>(ConversationTrigger.TtsStreamChunkReceived);
        var errorOccurredTrigger    = _stateMachine.SetTriggerParameters<Exception>(ConversationTrigger.ErrorOccurred);

        _stateMachine.Configure(ConversationState.Initial)
                     .Permit(ConversationTrigger.InitializeRequested, ConversationState.Initializing)
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error);

        _stateMachine.Configure(ConversationState.Initializing)
                     .OnEntryAsync(InitializeSessionAsync, "Initialize Session Resources")
                     .Permit(ConversationTrigger.InitializeComplete, ConversationState.Idle)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error)
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended);

        _stateMachine.Configure(ConversationState.Idle)
                     .OnEntry(HandleIdle, "Handle Idle State")
                     .Ignore(ConversationTrigger.LlmStreamEnded)
                     .Ignore(ConversationTrigger.TtsStreamEnded)
                     .Ignore(ConversationTrigger.AudioStreamEnded)
                     .Permit(ConversationTrigger.InputDetected, ConversationState.Listening)
                     .Permit(ConversationTrigger.InputFinalized, ConversationState.ProcessingInput)
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error);

        _stateMachine.Configure(ConversationState.Listening)
                     .SubstateOf(ConversationState.Idle)
                     .Permit(ConversationTrigger.InputFinalized, ConversationState.ProcessingInput)
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Permit(ConversationTrigger.PauseRequested, ConversationState.Paused)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error)
                     .Ignore(ConversationTrigger.InputDetected);

        _stateMachine.Configure(ConversationState.ActiveTurn)
                     .PermitIf(_inputDetectedTrigger, ConversationState.Interrupted,
                               ShouldAllowBargeIn, "Barge-In")
                     .PermitIf(_inputFinalizedTrigger, ConversationState.Interrupted,
                               ShouldAllowBargeIn, "Barge-In");

        _stateMachine.Configure(ConversationState.ProcessingInput)
                     .SubstateOf(ConversationState.ActiveTurn)
                     .OnEntryFromAsync(_inputFinalizedTrigger, PrepareLlmRequestAsync, "Process Input and Call LLM")
                     .Ignore(ConversationTrigger.LlmStreamEnded)
                     .Ignore(ConversationTrigger.TtsStreamEnded)
                     .Ignore(ConversationTrigger.AudioStreamEnded)
                     .Permit(ConversationTrigger.LlmRequestSent, ConversationState.WaitingForLlm)
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error);

        _stateMachine.Configure(ConversationState.WaitingForLlm)
                     .SubstateOf(ConversationState.ActiveTurn)
                     .OnEntryFrom(ConversationTrigger.LlmRequestSent, HandleLlmStreamRequested, "Start LLM")
                     .InternalTransition(ConversationTrigger.TtsRequestSent, HandleTtsStreamRequest)
                     .Permit(ConversationTrigger.LlmStreamStarted, ConversationState.StreamingResponse)
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error);

        _stateMachine.Configure(ConversationState.StreamingResponse)
                     .SubstateOf(ConversationState.ActiveTurn)
                     .InternalTransitionAsync(llmChunkReceivedTrigger, HandleLlmStreamChunkReceived)
                     .InternalTransitionAsync(ttsChunkReceivedTrigger, HandleTtsStreamChunkReceived)
                     .InternalTransition(ConversationTrigger.TtsStreamEnded, HandleTtsStreamEnded)
                     .InternalTransition(ConversationTrigger.LlmStreamEnded, HandleLlmStreamEnded)
                     .Ignore(ConversationTrigger.TtsStreamStarted)
                     .Permit(ConversationTrigger.AudioStreamStarted, ConversationState.Speaking)
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error);

        _stateMachine.Configure(ConversationState.Speaking)
                     .SubstateOf(ConversationState.ActiveTurn)
                     .OnExit(CommitSpokenText)
                     .InternalTransitionAsync(llmChunkReceivedTrigger, HandleLlmStreamChunkReceived)
                     .InternalTransitionAsync(ttsChunkReceivedTrigger, HandleTtsStreamChunkReceived)
                     .InternalTransition(ConversationTrigger.TtsStreamStarted, HandleTtsStreamRequest)
                     .InternalTransition(ConversationTrigger.TtsStreamEnded, HandleTtsStreamEnded)
                     .InternalTransition(ConversationTrigger.LlmStreamEnded, HandleLlmStreamEnded)
                     .Permit(ConversationTrigger.AudioStreamEnded, ConversationState.Idle)
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error);

        _stateMachine.Configure(ConversationState.Interrupted)
                     .OnEntryFromAsync(_inputDetectedTrigger, HandleInterruptionCancelAndRefireAsync, "Handle Detected Interruption, Cancel, Refire")
                     .OnEntryFromAsync(_inputFinalizedTrigger, HandleInterruptionCancelAndRefireAsync, "Handle Finalized Interruption, Cancel, Refire")
                     .OnExitAsync(CancelCurrentTurnProcessingAsync)
                     .Ignore(ConversationTrigger.TtsRequestSent)
                     .Ignore(ConversationTrigger.LlmStreamChunkReceived)
                     .Ignore(ConversationTrigger.TtsStreamChunkReceived)
                     .Ignore(ConversationTrigger.LlmStreamStarted)
                     .Ignore(ConversationTrigger.TtsStreamStarted)
                     .Ignore(ConversationTrigger.AudioStreamStarted)
                     .Ignore(ConversationTrigger.LlmStreamEnded)
                     .Ignore(ConversationTrigger.TtsStreamEnded)
                     .Ignore(ConversationTrigger.AudioStreamEnded)
                     .Permit(ConversationTrigger.InputDetected, ConversationState.Listening)
                     .Permit(ConversationTrigger.InputFinalized, ConversationState.ProcessingInput)
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error);

        _stateMachine.Configure(ConversationState.Paused)
                     .OnEntryAsync(PauseActivitiesAsync, "Pause Adapters/Activities")
                     .Permit(ConversationTrigger.ResumeRequested, ConversationState.Idle)
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error)
                     .OnExitAsync(ResumeActivitiesAsync, "Resume Adapters/Activities");

        _stateMachine.Configure(ConversationState.Error)
                     .OnEntryFromAsync(errorOccurredTrigger, HandleErrorAsync, "Log Error and Update Context")
                     .OnEntryAsync(CancelCurrentTurnProcessingAsync, "Ensure Pipeline Cancelled on Error")
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Ignore(ConversationTrigger.ErrorOccurred);

        _stateMachine.Configure(ConversationState.Ended)
                     .OnEntryAsync(CancelCurrentTurnProcessingAsync, "Ensure Pipeline Cancelled on End")
                     .OnEntryAsync(CleanupSessionAsync, "Stop Adapters and Cleanup Resources");

        _stateMachine.OnUnhandledTriggerAsync(async (state, trigger, unmetGuards) =>
                                              {
                                                  if ( _stateMachine.IsInState(ConversationState.Ended) || _stateMachine.IsInState(ConversationState.Error) )
                                                  {
                                                      return;
                                                  }

                                                  if ( unmetGuards.Count != 0 && unmetGuards.First() == "Barge-In" )
                                                  {
                                                      return;
                                                  }

                                                  _logger.LogWarning("{SessionId} | Unhandled trigger '{Trigger}' in state '{State}'. Unmet guards: [{UnmetGuards}]",
                                                                     SessionId, trigger, state, string.Join(", ", unmetGuards ?? Array.Empty<string>()));

                                                  await _stateMachine.FireAsync(ConversationTrigger.ErrorOccurred, new InvalidOperationException($"Unhandled trigger {trigger} in state {state}"));
                                              });

        _stateMachine.OnTransitionedAsync(async transition =>
                                          {
                                              _logger.LogDebug("{SessionId} | Transitioned: {SourceState} --({Trigger})--> {DestinationState}",
                                                               SessionId, transition.Source, transition.Trigger, transition.Destination);

                                              await ValueTask.CompletedTask;
                                          });

        _stateMachine.OnTransitioned(LogState);
    }

    private void LogState(StateMachine<ConversationState, ConversationTrigger>.Transition transition)
    {
        if ( transition.Source == transition.Destination )
        {
            return;
        }

        var emoji = transition.Destination switch {
            ConversationState.Initializing => "🤖",
            ConversationState.Idle => "🥱",
            ConversationState.Listening => "🧐",
            ConversationState.ProcessingInput => "🤔",
            ConversationState.WaitingForLlm => "😑",
            ConversationState.StreamingResponse => "😃",
            ConversationState.Speaking => "🗣️",
            ConversationState.Interrupted => "😵",
            ConversationState.Paused => "🤐",
            ConversationState.Error => "😱",
            ConversationState.Ended => "🙃",
            _ => string.Empty
        };

        _logger.LogInformation("{SessionId} | {TurnId} | {Emoji} | {DestinationState}",
                               SessionId, _currentTurnId ?? Guid.Empty, emoji, transition.Destination);
    }

    private bool ShouldAllowBargeIn(IInputEvent inputEvent)
    {
        var context = new BargeInContext(
                                         _options,
                                         _stateMachine.State,
                                         inputEvent,
                                         SessionId,
                                         _currentTurnId
                                        );

        var allow = _bargeInStrategy.ShouldAllowBargeIn(context);

        _logger.LogDebug("{SessionId} | Barge-in check for trigger {TriggerType} in state {State}. Strategy decided: {Allow}",
                         SessionId, inputEvent.GetType().Name, context.CurrentState, allow);

        return allow;
    }
}