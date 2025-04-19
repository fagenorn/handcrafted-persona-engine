using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;

using OpenAI.Chat;

using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;

using ChatMessage = PersonaEngine.Lib.Core.Conversation.Abstractions.Context.ChatMessage;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Context;

public class ConversationContext : IConversationContext
{
    private readonly IConversationCleanupStrategy? _cleanupStrategy;

    private readonly Dictionary<string, StringBuilder> _currentMessageBuffers = new();

    private readonly List<InteractionTurn> _history = new();

    private readonly Lock _lock = new();

    private readonly IDisposable? _optionsMonitorRegistration;

    private readonly Dictionary<string, ParticipantInfo> _participants;

    private readonly HashSet<string> _participantsReadyToCommit = new();

    private Guid _currentTurnId = Guid.Empty;

    private HashSet<string> _currentTurnParticipantIds = new();

    private string? _currentVisualContext;

    private ConversationContextOptions _options;

    private bool _turnInterrupted = false;

    private DateTimeOffset _turnStartTime;

    public ConversationContext(
        IEnumerable<ParticipantInfo>                initialParticipants,
        IOptionsMonitor<ConversationContextOptions> optionsMonitor,
        IConversationCleanupStrategy?               cleanupStrategy = null)
    {
        _participants    = initialParticipants.ToDictionary(p => p.Id);
        _options         = optionsMonitor.CurrentValue;
        _cleanupStrategy = cleanupStrategy ?? new MaxTurnsCleanupStrategy(100);
        _optionsMonitorRegistration = optionsMonitor.OnChange(newOptions =>
                                                              {
                                                                  lock (_lock)
                                                                  {
                                                                      if ( newOptions.SystemPromptFile != _options.SystemPromptFile )
                                                                      {
                                                                          PromptUtils.ClearCache();
                                                                      }

                                                                      _options = newOptions;
                                                                  }
                                                              });
    }

    public event EventHandler? ConversationUpdated;

    public IReadOnlyDictionary<string, ParticipantInfo> Participants
    {
        get
        {
            lock (_lock)
            {
                return new ReadOnlyDictionary<string, ParticipantInfo>(_participants);
            }
        }
    }

    public IReadOnlyList<InteractionTurn> History
    {
        get
        {
            lock (_lock)
            {
                return _history.AsReadOnly();
            }
        }
    }

    public string? CurrentVisualContext
    {
        get
        {
            lock (_lock)
            {
                return _currentVisualContext;
            }
        }
        set
        {
            lock (_lock)
            {
                _currentVisualContext = value;
                OnConversationUpdated();
            }
        }
    }

    public bool TryAddParticipant(ParticipantInfo participant)
    {
        lock (_lock)
        {
            var added = _participants.TryAdd(participant.Id, participant);
            if ( added )
            {
                OnConversationUpdated();
            }

            return added;
        }
    }

    public bool TryRemoveParticipant(string participantId)
    {
        lock (_lock)
        {
            var removed = _participants.Remove(participantId);
            if ( removed )
            {
                // Optional: Decide if removing a participant should affect ongoing turns
                // e.g., remove them from _currentTurnParticipantIds if turn is active?
                // Current implementation doesn't automatically remove from active turn.
                OnConversationUpdated();
            }

            return removed;
        }
    }

    #region Cleanup

    public void ApplyCleanupStrategy()
    {
        if ( _cleanupStrategy == null )
        {
            return;
        }

        lock (_lock)
        {
            if ( _cleanupStrategy.Cleanup(_history, _options, _participants) )
            {
                OnConversationUpdated();
            }
        }
    }

    public void ClearHistory()
    {
        lock (_lock)
        {
            if ( _history.Count <= 0 )
            {
                return;
            }

            _history.Clear();
            OnConversationUpdated();
        }
    }

    public void Dispose() { Dispose(true); }

    protected virtual void Dispose(bool disposing)
    {
        if ( disposing )
        {
            _optionsMonitorRegistration?.Dispose();
        }
    }

    #endregion

    #region Private Helpers

    protected virtual void OnConversationUpdated()
    {
        var handler = ConversationUpdated;
        handler?.Invoke(this, EventArgs.Empty);
    }

    private void InternalAbortTurn(bool raiseEvent = true)
    {
        var wasActive = _currentTurnId != Guid.Empty;

        _currentTurnId = Guid.Empty;
        _currentMessageBuffers.Clear();
        _participantsReadyToCommit.Clear();
        _currentTurnParticipantIds.Clear();
        _turnInterrupted = false;

        if ( wasActive && raiseEvent )
        {
            OnConversationUpdated();
        }
    }

    private void FinalizeCurrentTurn()
    {
        if ( _currentTurnId == Guid.Empty )
        {
            return;
        }

        var turn = _history.FirstOrDefault(t => t.TurnId == _currentTurnId);

        if ( turn != null )
        {
            turn.EndTime        = DateTimeOffset.UtcNow;
            turn.WasInterrupted = _turnInterrupted;
        }

        _currentTurnId = Guid.Empty;
        _currentMessageBuffers.Clear();
        _participantsReadyToCommit.Clear();
        _currentTurnParticipantIds.Clear();
        _turnInterrupted = false;
    }

    private InteractionTurn? CreatePendingTurnSnapshot()
    {
        if ( _currentTurnId == Guid.Empty || _currentMessageBuffers.Count == 0 )
        {
            return null;
        }

        var pendingMessages = new List<ChatMessage>();
        foreach ( var (participantId, buffer) in _currentMessageBuffers )
        {
            var text = buffer.ToString();

            if ( string.IsNullOrWhiteSpace(text) || !_participants.TryGetValue(participantId, out var participantInfo) )
            {
                continue;
            }

            var snapshotMessage = new ChatMessage(
                                                  Guid.NewGuid(),
                                                  participantId,
                                                  participantInfo.Name,
                                                  text,
                                                  _turnStartTime,
                                                  true,
                                                  participantInfo.Role
                                                 );

            pendingMessages.Add(snapshotMessage);
        }

        if ( pendingMessages.Count == 0 )
        {
            return null;
        }

        return new InteractionTurn(
                                   _currentTurnId,
                                   _currentTurnParticipantIds,
                                   _turnStartTime,
                                   null,
                                   pendingMessages,
                                   _turnInterrupted
                                  );
    }

    private void AddMessageToSkHistory(ChatHistory skChatHistory, ChatMessage message)
    {
        var role    = message.Role;
        var content = message.Text;

        if ( role != ChatMessageRole.Assistant && role != ChatMessageRole.System )
        {
            content = $"[{message.ParticipantName}]{message.Text}";

            role = ChatMessageRole.User;
        }

        skChatHistory.AddMessage(GetAuthorRole(role), content);
    }

    private AuthorRole GetAuthorRole(ChatMessageRole role)
    {
        return role switch {
            ChatMessageRole.System => AuthorRole.System,
            ChatMessageRole.Assistant => AuthorRole.Assistant,
            ChatMessageRole.User => AuthorRole.User,
            ChatMessageRole.Developer => AuthorRole.Developer,
            ChatMessageRole.Tool => AuthorRole.Tool,
            ChatMessageRole.Function => AuthorRole.Tool,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };
    }

    #endregion

    #region Message Management

    public bool TryUpdateMessage(Guid turnId, Guid messageId, string newText)
    {
        lock (_lock)
        {
            var turn    = _history.FirstOrDefault(t => t.TurnId == turnId);
            var message = turn?.Messages.FirstOrDefault(m => m.MessageId == messageId);
            if ( message != null && message.Text != newText )
            {
                message.Text = newText;
                OnConversationUpdated();

                return true;
            }

            return false;
        }
    }

    public bool TryDeleteMessage(Guid turnId, Guid messageId)
    {
        lock (_lock)
        {
            var turn = _history.FirstOrDefault(t => t.TurnId == turnId);
            if ( turn != null )
            {
                var messageToRemove = turn.Messages.FirstOrDefault(m => m.MessageId == messageId);
                if ( messageToRemove == null )
                {
                    return false;
                }

                var messageRemoved = turn.Messages.Remove(messageToRemove);
                var turnRemoved    = false;
                if ( turn.Messages.Count == 0 )
                {
                    turnRemoved = _history.Remove(turn);
                }

                if ( messageRemoved || turnRemoved )
                {
                    OnConversationUpdated();

                    return true;
                }

                return true;
            }

            return false;
        }
    }

    #endregion

    #region Turn Management

    public void StartTurn(Guid turnId, IEnumerable<string> participantIds)
    {
        lock (_lock)
        {
            if ( _currentTurnId != Guid.Empty && _currentMessageBuffers.Count > 0 )
            {
                Debug.WriteLine($"Warning: Starting new turn {turnId} while previous turn {_currentTurnId} was pending. Aborting previous turn.");
                InternalAbortTurn();
            }

            _currentTurnId             = turnId;
            _currentTurnParticipantIds = new HashSet<string>(participantIds.Distinct());

            foreach ( var id in _currentTurnParticipantIds )
            {
                if ( !_participants.ContainsKey(id) )
                {
                    InternalAbortTurn(false);

                    throw new ArgumentException($"Participant with ID '{id}' not found in the conversation context.", nameof(participantIds));
                }
            }

            _currentMessageBuffers.Clear();
            _participantsReadyToCommit.Clear();
            _turnStartTime   = DateTimeOffset.UtcNow;
            _turnInterrupted = false;

            foreach ( var id in _currentTurnParticipantIds )
            {
                _currentMessageBuffers[id] = new StringBuilder();
            }
        }
    }

    public void AppendToTurn(string participantId, string chunk)
    {
        lock (_lock)
        {
            if ( _currentTurnId == Guid.Empty )
            {
                throw new InvalidOperationException("Cannot append to turn: No turn is currently active.");
            }

            if ( !_currentTurnParticipantIds.Contains(participantId) )
            {
                throw new InvalidOperationException($"Participant '{participantId}' is not designated as part of the current turn (ID: {_currentTurnId}).");
            }

            if ( !_currentMessageBuffers.TryGetValue(participantId, out var buffer) )
            {
                buffer                                = new StringBuilder();
                _currentMessageBuffers[participantId] = buffer;
                Debug.WriteLine($"Warning: Buffer for participant {participantId} was missing in AppendToTurn.");
            }

            buffer.Append(chunk);
            OnConversationUpdated();
        }
    }

    public string GetPendingMessageText(string participantId)
    {
        lock (_lock)
        {
            if ( _currentTurnId == Guid.Empty )
            {
                return string.Empty;
            }

            if ( _currentMessageBuffers.TryGetValue(participantId, out var buffer) )
            {
                return buffer.ToString();
            }

            return string.Empty;
        }
    }

    public void CompleteTurnPart(string participantId, bool interrupted = false)
    {
        lock (_lock)
        {
            if ( _currentTurnId == Guid.Empty )
            {
                return;
            }

            if ( !_currentTurnParticipantIds.Contains(participantId) )
            {
                return;
            }

            if ( _participantsReadyToCommit.Contains(participantId) )
            {
                return;
            }

            if ( !_currentMessageBuffers.TryGetValue(participantId, out var buffer) || buffer.Length == 0 )
            {
                Debug.WriteLine($"Debug: CompleteTurnPart called for participant {participantId} in turn {_currentTurnId} with no message content.");
            }

            if ( _participants.TryGetValue(participantId, out var participantInfo) )
            {
                var text = buffer!.ToString();
                var message = new ChatMessage(
                                              Guid.NewGuid(),
                                              participantId,
                                              participantInfo.Name,
                                              text,
                                              DateTimeOffset.UtcNow,
                                              false,
                                              participantInfo.Role
                                             );

                var turn = _history.FirstOrDefault(t => t.TurnId == _currentTurnId);
                if ( turn == null )
                {
                    turn = new InteractionTurn(
                                               _currentTurnId,
                                               _currentTurnParticipantIds,
                                               _turnStartTime,
                                               null,
                                               new List<ChatMessage> { message },
                                               interrupted
                                              );

                    _history.Add(turn);
                    ApplyCleanupStrategy();
                }
                else
                {
                    turn.Messages.Add(message);
                    turn.WasInterrupted = turn.WasInterrupted || interrupted;
                }

                _currentMessageBuffers.Remove(participantId);
            }
            else
            {
                return;
            }

            _participantsReadyToCommit.Add(participantId);
            _turnInterrupted = _turnInterrupted || interrupted;

            if ( _participantsReadyToCommit.IsSupersetOf(_currentTurnParticipantIds) )
            {
                FinalizeCurrentTurn();
            }

            OnConversationUpdated();
        }
    }

    public void AbortTurn()
    {
        lock (_lock)
        {
            InternalAbortTurn();
        }
    }

    #endregion

    #region History & Projection

    public InteractionTurn? PendingTurn
    {
        get
        {
            lock (_lock)
            {
                return CreatePendingTurnSnapshot();
            }
        }
    }

    public IReadOnlyList<InteractionTurn> GetProjectedHistory()
    {
        lock (_lock)
        {
            var projectedHistory = new List<InteractionTurn>(_history);
            var pendingTurn      = CreatePendingTurnSnapshot();
            if ( pendingTurn != null )
            {
                projectedHistory.Add(pendingTurn);
            }

            return projectedHistory.AsReadOnly();
        }
    }

    public ChatHistory GetSemanticKernelChatHistory(bool includePendingTurn = true)
    {
        lock (_lock)
        {
            var skChatHistory = new ChatHistory();

            // 1. Add System Prompt (from options)
            if ( !string.IsNullOrWhiteSpace(_options.SystemPrompt) )
            {
                skChatHistory.AddSystemMessage(_options.SystemPrompt);
            }
            else if ( !string.IsNullOrWhiteSpace(_options.SystemPromptFile) && PromptUtils.TryGetPrompt(_options.SystemPromptFile, out var systemPrompt) )
            {
                skChatHistory.AddSystemMessage(systemPrompt!);
            }

            // 2. Add Metadata Message
            var metadata           = new { topics = _options.Topics, context = _options.CurrentContext ?? string.Empty, visual_context = _currentVisualContext ?? string.Empty };
            var serializedMetadata = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            skChatHistory.AddUserMessage(serializedMetadata);

            // 3. Add Committed History Messages
            foreach ( var message in _history.SelectMany(turn => turn.Messages) )
            {
                AddMessageToSkHistory(skChatHistory, message);
            }

            // 4. Optionally Add Pending Turn Messages
            if ( includePendingTurn )
            {
                var pendingTurn = CreatePendingTurnSnapshot();
                if ( pendingTurn == null )
                {
                    return skChatHistory;
                }

                foreach ( var message in pendingTurn.Messages )
                {
                    AddMessageToSkHistory(skChatHistory, message);
                }
            }

            return skChatHistory;
        }
    }

    #endregion
}