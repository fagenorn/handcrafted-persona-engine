using Microsoft.SemanticKernel.ChatCompletion;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Context;

public interface IConversationContext : IDisposable
{
    IReadOnlyDictionary<string, ParticipantInfo> Participants { get; }

    IReadOnlyList<InteractionTurn> History { get; }

    string? CurrentVisualContext { get; }

    InteractionTurn? PendingTurn { get; }

    bool TryAddParticipant(ParticipantInfo participant);

    bool TryRemoveParticipant(string participantId);

    void StartTurn(Guid turnId, IEnumerable<string> participantIds);

    void AppendToTurn(string participantId, string chunk);

    string GetPendingMessageText(string participantId);

    void CompleteTurnPart(string participantId, bool interrupted = false);

    IReadOnlyList<InteractionTurn> GetProjectedHistory();

    public void AbortTurn();

    ChatHistory GetSemanticKernelChatHistory(bool includePendingTurn = true);

    bool TryUpdateMessage(Guid turnId, Guid messageId, string newText);

    bool TryDeleteMessage(Guid turnId, Guid messageId);

    void ApplyCleanupStrategy();

    void ClearHistory();

    event EventHandler? ConversationUpdated;

    /// <summary>Fired when a new turn begins (StartTurn).</summary>
    event EventHandler<TurnStartedEventArgs>? TurnStarted;

    /// <summary>Fired on every AppendToTurn call with the delta + accumulated text + parsed emotion.</summary>
    event EventHandler<MessageAppendedEventArgs>? MessageAppended;

    /// <summary>Fired once the whole turn finalizes (all participants committed).</summary>
    event EventHandler<TurnCompletedEventArgs>? TurnCompleted;
}