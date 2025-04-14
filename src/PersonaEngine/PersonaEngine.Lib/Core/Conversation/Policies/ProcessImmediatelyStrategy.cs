using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Core.Conversation.Contracts.Events;

namespace PersonaEngine.Lib.Core.Conversation.Policies;

/// <summary>
///     A turn-taking strategy that processes utterances immediately ONLY if the system is idle.
///     Ignores utterances if the system is Processing or Speaking.
/// </summary>
public class ProcessImmediatelyStrategy(ILogger<ProcessImmediatelyStrategy> logger) : ITurnTakingStrategy
{
    // Optional logger

    public TurnTakingAction DecideAction(UserUtteranceCompleted utterance, ConversationOrchestrator.State currentState)
    {
        switch ( currentState )
        {
            case ConversationOrchestrator.State.Idle:
                logger?.LogTrace("Strategy: State is Idle, allowing ProcessNow.");

                return TurnTakingAction.ProcessNow;

            case ConversationOrchestrator.State.Processing:
            case ConversationOrchestrator.State.Speaking:
            case ConversationOrchestrator.State.Cancelling:
                logger?.LogTrace("Strategy: State is {State}, requesting Ignore.", currentState);

                return TurnTakingAction.Ignore;

            default:
                logger?.LogWarning("Strategy: Unknown state {State}, requesting Ignore.", currentState);

                return TurnTakingAction.Ignore;
        }
    }
}