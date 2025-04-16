namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Context;

public interface IConversationContext
{
    IReadOnlyDictionary<string, ParticipantInfo> Participants { get; }

    IEnumerable<InteractionTurn> History { get; }

    string? CurrentVisualContext { get; }

    bool TryAddParticipant(ParticipantInfo participant);

    bool TryRemoveParticipant(string participantId);
    
    void SetCurrentVisualContext(string? visualContext);

    public void StartTurn(Guid turnId, IEnumerable<string> participantIds);

    public void AppendToTurn(string participantId, string chunk);

    public void CompleteTurn(string participantId, bool interrupted);

    IEnumerable<InteractionTurn> GetProjectedHistory();
    
    public void AbortTurn();
}