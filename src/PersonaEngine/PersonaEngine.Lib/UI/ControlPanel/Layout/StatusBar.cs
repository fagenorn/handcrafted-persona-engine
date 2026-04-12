using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Top status strip that shows current conversation state.
/// </summary>
public sealed class StatusBar(IConversationOrchestrator orchestrator)
{
    private const int MaxSpeechSnippetLength = 80;

    public void Render()
    {
        var (_, barHeight) = Ui.PeekContext();
        var (color, label, speechText) = GetConversationState();

        ImGui.SetCursorPosY((barHeight - ImGui.GetTextLineHeight()) * 0.5f);

        ImGuiHelpers.StatusDot(color);
        ImGui.SameLine(0f, 8f);

        ImGui.SetCursorPosY((barHeight - ImGui.GetTextLineHeight()) * 0.5f);
        ImGui.TextUnformatted(label);

        if (speechText is not null)
        {
            ImGui.SameLine(0f, 12f);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted(GetCurrentSpeechSnippet(speechText));
            ImGui.PopStyleColor();
        }
    }

    private (Vector4 Color, string Label, string? SpeechText) GetConversationState()
    {
        var sessionIds = orchestrator.GetActiveSessionIds().ToList();

        if (sessionIds.Count == 0)
        {
            return (Theme.TextSecondary, "No Session", null);
        }

        // Use the first active session
        IConversationSession? session = null;
        try
        {
            session = orchestrator.GetSession(sessionIds[0]);
        }
        catch (KeyNotFoundException)
        {
            return (Theme.TextSecondary, "No Session", null);
        }

        // Derive a rough state from the pending turn
        var pendingTurn = session.Context.PendingTurn;

        if (pendingTurn is not null)
        {
            // There is an active turn in progress — determine from messages
            var hasSpeech = pendingTurn.Messages.Count > 0;
            if (hasSpeech)
            {
                var lastMessage = pendingTurn.Messages[^1];
                var snippetText = lastMessage.Text;

                return (
                    Theme.AccentSecondary,
                    "Speaking",
                    string.IsNullOrEmpty(snippetText) ? null : snippetText
                );
            }

            return (Theme.Warning, "Thinking...", null);
        }

        // Fallback: session is idle/listening
        var sessionCount = sessionIds.Count;
        var label = sessionCount == 1 ? "Listening" : $"Listening ({sessionCount})";

        return (Theme.Success, label, null);
    }

    private static string GetCurrentSpeechSnippet(string text)
    {
        if (text.Length <= MaxSpeechSnippetLength)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, MaxSpeechSnippetLength), "...");
    }
}
