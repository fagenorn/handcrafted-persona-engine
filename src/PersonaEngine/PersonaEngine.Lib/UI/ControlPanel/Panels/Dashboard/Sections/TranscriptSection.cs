using System.Numerics;
using Hexa.NET.ImGui;
using OpenAI.Chat;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using ConversationChatMessage = PersonaEngine.Lib.Core.Conversation.Abstractions.Context.ChatMessage;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Sections;

/// <summary>
///     Scrolling conversation transcript showing history and pending (in-progress) turns.
/// </summary>
public sealed class TranscriptSection(IConversationOrchestrator orchestrator)
{
    private const float Padding = 8f;

    public void Render(float dt, float availableHeight)
    {
        ImGuiHelpers.SectionHeader("Conversation");

        var scrollHeight = MathF.Max(availableHeight, 100f);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Padding, Padding));

        if (ImGui.BeginChild("##Messages", new Vector2(0f, scrollHeight), ImGuiChildFlags.Borders))
        {
            var session = TryGetActiveSession();

            if (session is null)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
                ImGui.TextUnformatted("No active conversation.");
                ImGui.PopStyleColor();
            }
            else
            {
                RenderHistory(session.Context);
            }

            if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 20f)
                ImGui.SetScrollHereY(1f);
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    private static void RenderHistory(IConversationContext context)
    {
        var history = context.History;

        foreach (var turn in history)
        {
            foreach (var message in turn.Messages)
                RenderMessage(message);
        }

        var pending = context.PendingTurn;

        if (pending is not null)
        {
            foreach (var message in pending.Messages)
                RenderMessage(message);
        }
    }

    private static void RenderMessage(ConversationChatMessage message)
    {
        var nameColor =
            message.Role == ChatMessageRole.User ? Theme.AccentPrimary : Theme.AccentSecondary;

        ImGui.PushStyleColor(ImGuiCol.Text, nameColor);
        ImGui.TextUnformatted(message.ParticipantName);
        ImGui.PopStyleColor();

        ImGui.SameLine(0f, 6f);

        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(message.Text);
        ImGui.PopTextWrapPos();

        ImGui.Spacing();
    }

    private IConversationSession? TryGetActiveSession()
    {
        var sessionIds = orchestrator.GetActiveSessionIds().ToList();

        if (sessionIds.Count == 0)
            return null;

        try
        {
            return orchestrator.GetSession(sessionIds[0]);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }
}
