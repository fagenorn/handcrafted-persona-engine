using System.Diagnostics;
using System.Numerics;
using Hexa.NET.ImGui;
using OpenAI.Chat;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using ConversationChatMessage = PersonaEngine.Lib.Core.Conversation.Abstractions.Context.ChatMessage;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels;

/// <summary>
///     Dashboard panel showing system health, conversation transcript, and session stats.
/// </summary>
public sealed class Dashboard(IConversationOrchestrator orchestrator)
{
    private static readonly (string Name, string StatusText)[] _healthCards =
    [
        ("Microphone", "OK"),
        ("LLM", "OK"),
        ("TTS", "OK"),
        ("Spout", "OK"),
    ];

    private readonly Stopwatch _uptime = Stopwatch.StartNew();

    public void Render()
    {
        RenderSystemHealth();
        RenderTranscript();
        RenderSessionStats();
    }

    // ── System Health ────────────────────────────────────────────────────────────

    private static void RenderSystemHealth()
    {
        ImGuiHelpers.SectionHeader("System Health");

        var availableWidth = ImGui.GetContentRegionAvail().X;
        var gaps = (_healthCards.Length - 1) * ImGui.GetStyle().ItemSpacing.X;
        var cardWidth = MathF.Max(1f, (availableWidth - gaps) / _healthCards.Length);

        for (var i = 0; i < _healthCards.Length; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine();
            }

            RenderHealthCard(_healthCards[i].Name, _healthCards[i].StatusText, cardWidth);
        }
    }

    private static void RenderHealthCard(string name, string statusText, float cardWidth)
    {
        var cardSize = new Vector2(cardWidth, 64f);

        if (!ImGui.BeginChild(name, cardSize, ImGuiChildFlags.Borders))
        {
            ImGui.EndChild();

            return;
        }

        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted(name);
        ImGui.PopStyleColor();

        ImGui.SameLine(0f, 8f);
        ImGuiHelpers.StatusDot(Theme.Success);

        ImGui.TextUnformatted(statusText);

        ImGui.EndChild();
    }

    // ── Conversation Transcript ──────────────────────────────────────────────────

    private void RenderTranscript()
    {
        ImGuiHelpers.SectionHeader("Conversation");

        // Leave room for the stats section below (~120px)
        var availableHeight = ImGui.GetContentRegionAvail().Y - 120f;
        var transcriptSize = new Vector2(0f, availableHeight);

        if (!ImGui.BeginChild("##Transcript", transcriptSize, ImGuiChildFlags.Borders))
        {
            ImGui.EndChild();

            return;
        }

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

        // Auto-scroll to bottom when near bottom
        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 20f)
        {
            ImGui.SetScrollHereY(1f);
        }

        ImGui.EndChild();
    }

    private static void RenderHistory(IConversationContext context)
    {
        var history = context.History;

        foreach (var turn in history)
        {
            foreach (var message in turn.Messages)
            {
                RenderMessage(message);
            }
        }

        // Also show any pending (in-progress) turn
        var pending = context.PendingTurn;

        if (pending is not null)
        {
            foreach (var message in pending.Messages)
            {
                RenderMessage(message);
            }
        }
    }

    private static void RenderMessage(ConversationChatMessage message)
    {
        // Choose color based on role: user = accent primary, assistant = accent secondary
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

    // ── Session Stats ────────────────────────────────────────────────────────────

    private void RenderSessionStats()
    {
        ImGuiHelpers.SectionHeader("Session");

        if (!ImGui.BeginTable("##Stats", 4, ImGuiTableFlags.SizingStretchSame))
        {
            return;
        }

        ImGui.TableNextRow();

        RenderStatCell("Uptime", FormatUptime(_uptime.Elapsed));
        RenderStatCell("Turns", "--");
        RenderStatCell("Avg Response", "--");
        RenderStatCell("Interruptions", "--");

        ImGui.EndTable();
    }

    private static void RenderStatCell(string label, string value)
    {
        ImGui.TableNextColumn();

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();

        ImGui.TextUnformatted(value);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private IConversationSession? TryGetActiveSession()
    {
        var sessionIds = orchestrator.GetActiveSessionIds().ToList();

        if (sessionIds.Count == 0)
        {
            return null;
        }

        try
        {
            return orchestrator.GetSession(sessionIds[0]);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static string FormatUptime(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return string.Create(
                null,
                stackalloc char[16],
                $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
            );
        }

        return string.Create(
            null,
            stackalloc char[8],
            $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
        );
    }
}
