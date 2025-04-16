using System.Text;

using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Context;

public class ConversationContext : IConversationContext
{
    private readonly List<InteractionTurn> _history = new();

    private readonly Lock _lock = new();

    private readonly HashSet<string> _readyToCommit = new();

    private Dictionary<string, StringBuilder> _currentBuffers = new();

    private Guid _currentTurnId;

    private HashSet<string> _currentTurnParticipantIds = new();

    private DateTimeOffset _turnStart;

    public ConversationContext(IEnumerable<ParticipantInfo> participants) { Participants = participants.ToDictionary(p => p.Id); }

    public IReadOnlyDictionary<string, ParticipantInfo> Participants { get; private set; }

    public IEnumerable<InteractionTurn> History
    {
        get
        {
            lock (_lock)
            {
                return _history.ToList();
            }
        }
    }

    public string? CurrentVisualContext { get; private set; }

    public bool TryAddParticipant(ParticipantInfo participant)
    {
        lock (_lock)
        {
            if ( Participants.ContainsKey(participant.Id) )
            {
                return false;
            }

            var dict = Participants.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            dict[participant.Id] = participant;
            Participants         = dict;

            return true;
        }
    }

    public bool TryRemoveParticipant(string participantId)
    {
        lock (_lock)
        {
            if ( !Participants.ContainsKey(participantId) )
            {
                return false;
            }

            var dict = Participants.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            dict.Remove(participantId);
            Participants = dict;

            return true;
        }
    }

    public void SetCurrentVisualContext(string? visualContext)
    {
        lock (_lock)
        {
            CurrentVisualContext = visualContext;
        }
    }

    public void StartTurn(Guid turnId, IEnumerable<string> participantIds)
    {
        lock (_lock)
        {
            _currentTurnId             = turnId;
            _currentTurnParticipantIds = new HashSet<string>(participantIds);
            _currentBuffers            = _currentTurnParticipantIds.ToDictionary(id => id, _ => new StringBuilder());
            _readyToCommit.Clear();
            _turnStart = DateTimeOffset.UtcNow;
        }
    }

    public void AppendToTurn(string participantId, string chunk)
    {
        lock (_lock)
        {
            if ( !_currentBuffers.TryGetValue(participantId, out var buffer) )
            {
                throw new InvalidOperationException($"Participant '{participantId}' is not part of the current turn (ID: {_currentTurnId}).");
            }

            buffer.Append(chunk);
        }
    }
    
    public string PendingChunk(string participantId)
    {
        lock (_lock)
        {
            if ( !_currentBuffers.TryGetValue(participantId, out var buffer) )
            {
                throw new InvalidOperationException($"Participant '{participantId}' is not part of the current turn (ID: {_currentTurnId}).");
            }

            return buffer.ToString();
        }
    }

    public void CompleteTurn(string participantId, bool interrupted)
    {
        lock (_lock)
        {
            if ( !_currentBuffers.ContainsKey(participantId) )
            {
                return;
            }

            _readyToCommit.Add(participantId);
            if ( _readyToCommit.SetEquals(_currentTurnParticipantIds) )
            {
                CommitTurn(interrupted);
            }
        }
    }

    public void AbortTurn()
    {
        lock (_lock)
        {
            _currentBuffers.Clear();
            _readyToCommit.Clear();
        }
    }

    public IEnumerable<InteractionTurn> GetProjectedHistory()
    {
        lock (_lock)
        {
            var projectedHistory = _history.ToList();

            if ( _currentTurnId == Guid.Empty || _currentBuffers.Count == 0 )
            {
                return projectedHistory;
            }

            var projectedResponses = _currentBuffers.Where(x => !string.IsNullOrWhiteSpace(x.Value.ToString())).ToDictionary(
                                                                                                                             kvp => kvp.Key,
                                                                                                                             kvp => new LlmResponseDetails(
                                                                                                                                                           kvp.Value.ToString(),
                                                                                                                                                           false,
                                                                                                                                                           Participants[kvp.Key].Role
                                                                                                                                                          )
                                                                                                                            );

            var pendingTurnToCommit = new InteractionTurn(
                                                          _currentTurnId,
                                                          _currentTurnParticipantIds,
                                                          _turnStart,
                                                          DateTimeOffset.UtcNow,
                                                          projectedResponses
                                                         );

            projectedHistory.Add(pendingTurnToCommit);

            return projectedHistory;
        }
    }

    private void CommitTurn(bool interrupted)
    {
        var responses = _currentBuffers.Where(x => !string.IsNullOrWhiteSpace(x.Value.ToString())).ToDictionary(
                                                                                                                kvp => kvp.Key,
                                                                                                                kvp => new LlmResponseDetails(
                                                                                                                                              kvp.Value.ToString(),
                                                                                                                                              interrupted,
                                                                                                                                              Participants[kvp.Key].Role
                                                                                                                                             )
                                                                                                               );

        var turn = new InteractionTurn(
                                       _currentTurnId,
                                       _currentTurnParticipantIds,
                                       _turnStart,
                                       DateTimeOffset.UtcNow,
                                       responses
                                      );

        _history.Add(turn);

        _currentBuffers.Clear();
        _readyToCommit.Clear();
    }
}