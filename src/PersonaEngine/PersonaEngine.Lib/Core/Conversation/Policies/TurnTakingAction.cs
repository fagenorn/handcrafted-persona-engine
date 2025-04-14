namespace PersonaEngine.Lib.Core.Conversation.Policies;

/// <summary>
/// Defines the action to be taken for an incoming utterance based on the current state.
/// </summary>
public enum TurnTakingAction
{
    /// <summary>
    /// Process the utterance immediately.
    /// </summary>
    ProcessNow,

    /// <summary>
    /// Queue the utterance for later processing (if queuing is implemented).
    /// </summary>
    Queue,

    /// <summary>
    /// Ignore the utterance.
    /// </summary>
    Ignore
}