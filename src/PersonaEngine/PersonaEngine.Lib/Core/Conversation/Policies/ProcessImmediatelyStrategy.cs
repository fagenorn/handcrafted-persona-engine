using PersonaEngine.Lib.Core.Conversation.Contracts.Events;

namespace PersonaEngine.Lib.Core.Conversation.Policies;

/// <summary>
///     A turn-taking strategy that processes utterances immediately if the system is idle,
///     otherwise ignores them. This mirrors the default behavior before strategies.
/// </summary>
public class ProcessImmediatelyStrategy : ITurnTakingStrategy
{
    public TurnTakingAction DecideAction(UserUtteranceCompleted utterance, ConversationOrchestrator.State currentState)
    {
        return currentState == ConversationOrchestrator.State.Idle ? TurnTakingAction.ProcessNow : TurnTakingAction.Ignore;
    }
}