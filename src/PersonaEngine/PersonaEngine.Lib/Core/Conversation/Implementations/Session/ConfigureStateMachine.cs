using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Strategies;

using Stateless;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Session;

public partial class ConversationSession
{
    private readonly StateMachine<ConversationState, ConversationTrigger> _stateMachine;

    private void ConfigureStateMachine()
    {
        var inputDetectedTrigger    = _stateMachine.SetTriggerParameters<IInputEvent>(ConversationTrigger.InputDetected);
        var inputFinalizedTrigger   = _stateMachine.SetTriggerParameters<IInputEvent>(ConversationTrigger.InputFinalized);
        var llmChunkReceivedTrigger = _stateMachine.SetTriggerParameters<IOutputEvent>(ConversationTrigger.LlmStreamChunkReceived);
        var ttsChunkReceivedTrigger = _stateMachine.SetTriggerParameters<IOutputEvent>(ConversationTrigger.TtsStreamChunkReceived);
        var errorOccurredTrigger    = _stateMachine.SetTriggerParameters<Exception>(ConversationTrigger.ErrorOccurred);

        _stateMachine.Configure(ConversationState.Initial)
                     .Permit(ConversationTrigger.InitializeRequested, ConversationState.Initializing)
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error);

        _stateMachine.Configure(ConversationState.Initializing)
                     .OnEntry(LogState)
                     .OnEntryAsync(InitializeSessionAsync, "Initialize Session Resources")
                     .Permit(ConversationTrigger.InitializeComplete, ConversationState.Idle)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error)
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended);

        _stateMachine.Configure(ConversationState.Idle)
                     .OnEntry(LogState)
                     .OnEntry(HandleIdle, "Handle Idle State")
                     .Permit(ConversationTrigger.InputDetected, ConversationState.Listening)
                     .Permit(ConversationTrigger.InputFinalized, ConversationState.ProcessingInput)
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error);

        _stateMachine.Configure(ConversationState.Listening)
                     .SubstateOf(ConversationState.Idle)
                     .OnEntry(LogState)
                     .Permit(ConversationTrigger.InputFinalized, ConversationState.ProcessingInput)
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Permit(ConversationTrigger.PauseRequested, ConversationState.Paused)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error)
                     .Ignore(ConversationTrigger.InputDetected);

        _stateMachine.Configure(ConversationState.ProcessingInput)
                     .OnEntry(LogState)
                     .OnEntryFromAsync(inputFinalizedTrigger, PrepareLlmRequestAsync, "Process Input and Call LLM")
                     .Permit(ConversationTrigger.LlmRequestSent, ConversationState.WaitingForLlm)
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error);

        _stateMachine.Configure(ConversationState.WaitingForLlm)
                     .OnEntry(LogState)
                     .OnEntryFrom(ConversationTrigger.LlmRequestSent, HandleLlmStreamRequested, "Start LLM")
                     .InternalTransition(ConversationTrigger.TtsRequestSent, HandleTtsStreamRequest)
                     .Permit(ConversationTrigger.LlmStreamStarted, ConversationState.StreamingResponse)
                     .PermitIf(inputDetectedTrigger, ConversationState.Interrupted, e => _options.BargeInBehavior != BargeInStrategy.Ignore, "Handle Barge-In during Wait")
                     .PermitIf(inputFinalizedTrigger, ConversationState.Interrupted, e => _options.BargeInBehavior != BargeInStrategy.Ignore, "Handle Barge-In during Wait")
                     .IgnoreIf(ConversationTrigger.InputDetected, () => _options.BargeInBehavior == BargeInStrategy.Ignore, "Ignore Barge-In during Wait")
                     .IgnoreIf(ConversationTrigger.InputFinalized, () => _options.BargeInBehavior == BargeInStrategy.Ignore, "Ignore Barge-In during Wait")
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error);

        _stateMachine.Configure(ConversationState.StreamingResponse)
                     .OnEntry(LogState)
                     .InternalTransitionAsync(llmChunkReceivedTrigger, HandleLlmStreamChunkReceived)
                     .InternalTransitionAsync(ttsChunkReceivedTrigger, HandleTtsStreamChunkReceived)
                     .InternalTransition(ConversationTrigger.TtsStreamEnded, HandleTtsStreamEnded)
                     .InternalTransition(ConversationTrigger.LlmStreamEnded, HandleLlmStreamEnded)
                     .Ignore(ConversationTrigger.TtsStreamStarted)
                     .Permit(ConversationTrigger.AudioStreamStarted, ConversationState.Speaking)
                     .PermitIf(inputDetectedTrigger, ConversationState.Interrupted, e => _options.BargeInBehavior != BargeInStrategy.Ignore, "Handle Barge-In during Streaming")
                     .PermitIf(inputFinalizedTrigger, ConversationState.Interrupted, e => _options.BargeInBehavior != BargeInStrategy.Ignore, "Handle Barge-In during Streaming")
                     .IgnoreIf(ConversationTrigger.InputDetected, () => _options.BargeInBehavior == BargeInStrategy.Ignore, "Ignore Barge-In during Streaming")
                     .IgnoreIf(ConversationTrigger.InputFinalized, () => _options.BargeInBehavior == BargeInStrategy.Ignore, "Ignore Barge-In during Streaming")
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error);

        _stateMachine.Configure(ConversationState.Speaking)
                     .OnEntry(LogState)
                     .InternalTransitionAsync(llmChunkReceivedTrigger, HandleLlmStreamChunkReceived)
                     .InternalTransitionAsync(ttsChunkReceivedTrigger, HandleTtsStreamChunkReceived)
                     .InternalTransition(ConversationTrigger.TtsStreamStarted, HandleTtsStreamRequest)
                     .InternalTransition(ConversationTrigger.TtsStreamEnded, HandleTtsStreamEnded)
                     .InternalTransition(ConversationTrigger.LlmStreamEnded, HandleLlmStreamEnded)
                     .Permit(ConversationTrigger.AudioStreamEnded, ConversationState.Idle)
                     .PermitIf(inputDetectedTrigger, ConversationState.Interrupted, e => _options.BargeInBehavior != BargeInStrategy.Ignore, "Handle Barge-In during Playback")
                     .PermitIf(inputFinalizedTrigger, ConversationState.Interrupted, e => _options.BargeInBehavior != BargeInStrategy.Ignore, "Handle Barge-In during Playback")
                     .IgnoreIf(ConversationTrigger.InputDetected, () => _options.BargeInBehavior == BargeInStrategy.Ignore, "Ignore Barge-In during Playback")
                     .IgnoreIf(ConversationTrigger.InputFinalized, () => _options.BargeInBehavior == BargeInStrategy.Ignore, "Ignore Barge-In during Playback")
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error);

        _stateMachine.Configure(ConversationState.Interrupted)
                     .OnEntry(LogState)
                     .OnEntryFromAsync(inputDetectedTrigger, HandleInterruptionAsync, "Handle Barge-In Action")
                     .OnEntryFromAsync(inputFinalizedTrigger, HandleInterruptionAsync, "Handle Barge-In Action (Finalized)")
                     .OnEntryAsync(CancelCurrentTurnProcessingAsync, "Ensure Pipeline Cancelled on Interruption")
                     .Ignore(ConversationTrigger.LlmStreamStarted)
                     .Ignore(ConversationTrigger.LlmStreamChunkReceived)
                     .Ignore(ConversationTrigger.LlmStreamEnded)
                     .Ignore(ConversationTrigger.TtsRequestSent)
                     .Ignore(ConversationTrigger.TtsStreamStarted)
                     .Ignore(ConversationTrigger.TtsStreamChunkReceived)
                     .Ignore(ConversationTrigger.TtsStreamEnded)
                     .Ignore(ConversationTrigger.AudioStreamStarted)
                     .Ignore(ConversationTrigger.AudioStreamEnded)
                     .Permit(ConversationTrigger.InputFinalized, ConversationState.ProcessingInput)
                     .Permit(ConversationTrigger.InputDetected, ConversationState.Listening)
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error);

        _stateMachine.Configure(ConversationState.Paused)
                     .OnEntry(LogState)
                     .OnEntryAsync(PauseActivitiesAsync, "Pause Adapters/Activities")
                     .Permit(ConversationTrigger.ResumeRequested, ConversationState.Idle)
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Permit(ConversationTrigger.ErrorOccurred, ConversationState.Error)
                     .OnExitAsync(ResumeActivitiesAsync, "Resume Adapters/Activities");

        _stateMachine.Configure(ConversationState.Error)
                     .OnEntry(LogState)
                     .OnEntryFromAsync(errorOccurredTrigger, HandleErrorAsync, "Log Error and Update Context")
                     .OnEntryAsync(CancelCurrentTurnProcessingAsync, "Ensure Pipeline Cancelled on Error")
                     .Permit(ConversationTrigger.StopRequested, ConversationState.Ended)
                     .Ignore(ConversationTrigger.ErrorOccurred);

        _stateMachine.Configure(ConversationState.Ended)
                     .OnEntry(LogState)
                     .OnEntryAsync(CancelCurrentTurnProcessingAsync, "Ensure Pipeline Cancelled on End")
                     .OnEntryAsync(CleanupSessionAsync, "Stop Adapters and Cleanup Resources");

        _stateMachine.OnUnhandledTriggerAsync(async (state, trigger, unmetGuards) =>
                                              {
                                                  if ( _stateMachine.IsInState(ConversationState.Ended) || _stateMachine.IsInState(ConversationState.Error) )
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
    }

    private void LogState(StateMachine<ConversationState, ConversationTrigger>.Transition transition)
    {
        if ( transition.Source == transition.Destination )
        {
            return;
        }

        var emoji = transition.Destination switch {
            ConversationState.Initializing       => "🤖", 
            ConversationState.Idle               => "🥱", 
            ConversationState.Listening          => "🧐", 
            ConversationState.ProcessingInput    => "🤔",  
            ConversationState.WaitingForLlm      => "😑", 
            ConversationState.StreamingResponse  => "😃", 
            ConversationState.Speaking           => "🗣️", 
            ConversationState.Interrupted        => "😵", 
            ConversationState.Paused             => "🤐",  
            ConversationState.Error              => "😱", 
            ConversationState.Ended              => "🙃", 
            _                                     => string.Empty
        };

        _logger.LogInformation("{SessionId} | {TurnId} | {Emoji} | {DestinationState}",
                               SessionId, _currentTurnId ?? Guid.Empty, emoji, transition.Destination);
    }
}