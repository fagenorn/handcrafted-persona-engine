using Hexa.NET.ImGui;
using OpenAI.Chat;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using ConversationChatMessage = PersonaEngine.Lib.Core.Conversation.Abstractions.Context.ChatMessage;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Sections;

/// <summary>
///     Scrolling conversation transcript showing history and pending (in-progress) turns.
/// </summary>
public sealed class TranscriptSection(IConversationOrchestrator orchestrator)
{
    public void Render(float dt)
    {
        ImGuiHelpers.SectionHeader("Conversation");

        using (Ui.FillChild("##Messages", ImGuiChildFlags.Borders, padding: 8f))
        {
            if (!orchestrator.TryGetFirstActiveSession(out var session))
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
}
