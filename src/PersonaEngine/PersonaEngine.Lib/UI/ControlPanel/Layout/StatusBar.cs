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

    private float _elapsed;

    public void Render(float deltaTime)
    {
        _elapsed += deltaTime;

        var (color, label, speechText, isActive) = GetConversationState();

        // Glow pulse: oscillates between 0.08 and 0.20 opacity, disabled when inactive
        var glowAlpha = isActive ? 0.14f + 0.06f * MathF.Sin(_elapsed * 2.5f) : 0f;

        // Vertically center all content within the bar
        var contentH = ImGui.GetContentRegionAvail().Y;
        var textH = ImGui.GetTextLineHeight();
        var centerY = ImGui.GetCursorPosY() + (contentH - textH) * 0.5f;

        ImGui.SetCursorPosY(centerY);

        ImGuiHelpers.StatusDot(color, glowAlpha: glowAlpha);
        ImGui.SameLine(0f, 10f);

        // Label — explicit TextPrimary for maximum contrast
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextPrimary);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();

        if (speechText is not null)
        {
            ImGui.SameLine(0f, 14f);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
            ImGui.TextUnformatted($"\u2014 {GetCurrentSpeechSnippet(speechText)}");
            ImGui.PopStyleColor();
        }

        // Bottom accent border
        var drawList = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var borderY = winPos.Y + winSize.Y - 1f;
        var borderColor = ImGui.ColorConvertFloat4ToU32(Theme.AccentPrimary with { W = 0.25f });
        ImGui.AddLine(
            drawList,
            new Vector2(winPos.X, borderY),
            new Vector2(winPos.X + winSize.X, borderY),
            borderColor
        );
    }

    private (Vector4 Color, string Label, string? SpeechText, bool IsActive) GetConversationState()
    {
        var sessionIds = orchestrator.GetActiveSessionIds().ToList();

        if (sessionIds.Count == 0)
        {
            return (Theme.TextSecondary, "No Session", null, false);
        }

        // Use the first active session
        IConversationSession? session = null;
        try
        {
            session = orchestrator.GetSession(sessionIds[0]);
        }
        catch (KeyNotFoundException)
        {
            return (Theme.TextSecondary, "No Session", null, false);
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
                    string.IsNullOrEmpty(snippetText) ? null : snippetText,
                    true
                );
            }

            return (Theme.Warning, "Thinking...", null, true);
        }

        // Fallback: session is idle/listening
        var sessionCount = sessionIds.Count;
        var label = sessionCount == 1 ? "Listening" : $"Listening ({sessionCount})";

        return (Theme.Success, label, null, true);
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
