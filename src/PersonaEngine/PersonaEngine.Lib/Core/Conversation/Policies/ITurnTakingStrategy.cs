using PersonaEngine.Lib.Core.Conversation.Contracts.Events;

namespace PersonaEngine.Lib.Core.Conversation.Policies;

/// <summary>
///     Defines a strategy for deciding how to handle incoming user utterances based on the system state.
/// </summary>
public interface ITurnTakingStrategy
{
    /// <summary>
    ///     Determines the action to take for the incoming utterance.
    /// </summary>
    /// <param name="utterance">The newly completed user utterance.</param>
    /// <param name="currentState">The current state of the Conversation Orchestrator.</param>
    /// <returns>The action (ProcessNow, Queue, Ignore) to be taken.</returns>
    TurnTakingAction DecideAction(UserUtteranceCompleted utterance, ConversationOrchestrator.State currentState);
}