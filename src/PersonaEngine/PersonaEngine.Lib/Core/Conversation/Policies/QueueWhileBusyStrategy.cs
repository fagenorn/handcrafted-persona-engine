using PersonaEngine.Lib.Core.Conversation.Contracts.Events;

namespace PersonaEngine.Lib.Core.Conversation.Policies;

/// <summary>
///     A turn-taking strategy that processes utterances immediately if idle,
///     requests queuing if busy (Processing or Cancelling), and ignores otherwise.
///     Note: Actual queuing implementation is handled by the Orchestrator.
/// </summary>
public class QueueWhileBusyStrategy : ITurnTakingStrategy
{
    // Optional: Inject logger if needed for debugging strategy decisions
    // private readonly ILogger<QueueWhileBusyStrategy> _logger;
    // public QueueWhileBusyStrategy(ILogger<QueueWhileBusyStrategy> logger) { _logger = logger; }

    public TurnTakingAction DecideAction(UserUtteranceCompleted utterance, ConversationOrchestrator.State currentState)
    {
        return currentState switch {
            ConversationOrchestrator.State.Idle => TurnTakingAction.ProcessNow,
            ConversationOrchestrator.State.Processing or ConversationOrchestrator.State.Cancelling =>
                // Might check queue depth here
                TurnTakingAction.Queue,
            _ => TurnTakingAction.Ignore
        };
    }
}