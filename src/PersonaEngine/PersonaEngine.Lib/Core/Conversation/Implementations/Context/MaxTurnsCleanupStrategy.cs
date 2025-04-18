using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Context;

public class MaxTurnsCleanupStrategy : IConversationCleanupStrategy
{
    private readonly int _maxTurns;

    public MaxTurnsCleanupStrategy(int maxTurns)
    {
        if ( maxTurns < 0 )
        {
            throw new ArgumentOutOfRangeException(nameof(maxTurns), "Max turns cannot be negative.");
        }

        _maxTurns = maxTurns;
    }

    public void Cleanup(List<InteractionTurn> history, ConversationContextOptions options, IReadOnlyDictionary<string, ParticipantInfo> participants)
    {
        if ( _maxTurns == 0 || history.Count <= _maxTurns )
        {
            return;
        }

        var turnsToRemove = history.Count - _maxTurns;
        if ( turnsToRemove > 0 )
        {
            history.RemoveRange(0, turnsToRemove);
        }
    }
}