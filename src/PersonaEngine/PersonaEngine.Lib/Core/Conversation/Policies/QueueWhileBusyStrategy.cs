using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Core.Conversation.Contracts.Events;

namespace PersonaEngine.Lib.Core.Conversation.Policies;

/// <summary>
///     A turn-taking strategy that processes utterances immediately if idle,
///     requests queuing if busy (Processing or Speaking), and ignores otherwise.
///     Note: Actual queuing implementation is NOT handled here, only the request.
///     The current Orchestrator doesn't implement queuing, so Queue effectively means Ignore.
/// </summary>
public class QueueWhileBusyStrategy(ILogger<QueueWhileBusyStrategy> logger) : ITurnTakingStrategy
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
                logger?.LogTrace("Strategy: State is {State}, requesting Queue.", currentState);

                // NOTE: Orchestrator currently treats Queue as Ignore.
                // If queuing were implemented, this would signal it.
                return TurnTakingAction.Queue;

            case ConversationOrchestrator.State.Cancelling:
            default:
                logger?.LogTrace("Strategy: State is {State}, requesting Ignore.", currentState);

                return TurnTakingAction.Ignore;
        }
    }
}