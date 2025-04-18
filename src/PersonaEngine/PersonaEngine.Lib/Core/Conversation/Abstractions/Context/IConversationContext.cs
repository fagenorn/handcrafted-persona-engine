﻿using Microsoft.SemanticKernel.ChatCompletion;

namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Context;

public interface IConversationContext
{
    IReadOnlyDictionary<string, ParticipantInfo> Participants { get; }

    IReadOnlyList<InteractionTurn> History { get; }

    string? CurrentVisualContext { get; }

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
}