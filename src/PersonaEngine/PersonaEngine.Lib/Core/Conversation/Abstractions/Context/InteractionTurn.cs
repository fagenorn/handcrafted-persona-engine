using OpenAI.Chat;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Context;

public class InteractionTurn
{
    public InteractionTurn(Guid turnId, IEnumerable<string> participantIds, DateTimeOffset startTime, DateTimeOffset? endTime, IEnumerable<ChatMessage> messages, bool wasInterrupted)
    {
        TurnId         = turnId;
        ParticipantIds = participantIds.ToList().AsReadOnly();
        StartTime      = startTime;
        EndTime        = endTime;
        Messages       = messages.ToList();
        WasInterrupted = wasInterrupted;
    }

    private InteractionTurn(Guid turnId, IEnumerable<string> participantIds, DateTimeOffset startTime, IEnumerable<ChatMessage> messages)
    {
        TurnId         = turnId;
        ParticipantIds = participantIds.ToList().AsReadOnly();
        StartTime      = startTime;
        EndTime        = null;
        Messages       = messages.Select(m => new ChatMessage(m.MessageId, m.ParticipantId, m.ParticipantName, m.Text, m.Timestamp, m.IsPartial, m.Role)).ToList();
        WasInterrupted = false;
    }

    public Guid TurnId { get; }

    public IReadOnlyList<string> ParticipantIds { get; }

    public DateTimeOffset StartTime { get; }

    public DateTimeOffset? EndTime { get; internal set; }

    public List<ChatMessage> Messages { get; }

    public bool WasInterrupted { get; internal set; }

    internal InteractionTurn CreateSnapshot() { return new InteractionTurn(TurnId, ParticipantIds, StartTime, Messages); }
}