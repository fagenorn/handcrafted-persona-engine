using Hexa.NET.ImGui;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Bottom bar with quick action buttons for controlling the conversation.
/// </summary>
public sealed class ControlBar(IConversationOrchestrator orchestrator)
{
    private bool _isMuted;
    private bool _isPaused;

    public void Render()
    {
        var (_, barHeight) = Ui.PeekContext();
        ImGui.SetCursorPosY((barHeight - ImGui.GetFrameHeight()) * 0.5f);

        RenderPauseResumeButton();
        ImGui.SameLine(0f, 8f);
        RenderSkipButton();
        ImGui.SameLine(0f, 8f);
        RenderMuteButton();
    }

    private void RenderPauseResumeButton()
    {
        if (_isPaused)
        {
            if (ImGuiHelpers.PrimaryButton("Resume"))
            {
                _isPaused = false;
            }
        }
        else
        {
            if (ImGui.Button("Pause"))
            {
                _isPaused = true;
            }
        }

        ImGuiHelpers.Tooltip(_isPaused ? "Resume the conversation" : "Pause the conversation");
    }

    private void RenderSkipButton()
    {
        var isSpeaking = IsAnySpeaking();

        if (!isSpeaking)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Skip Response"))
        {
            if (isSpeaking)
            {
                StopActiveSessions();
            }
        }

        if (!isSpeaking)
        {
            ImGui.EndDisabled();
        }

        ImGuiHelpers.Tooltip("Skip the current response");
    }

    private void RenderMuteButton()
    {
        if (_isMuted)
        {
            if (ImGuiHelpers.DangerButton("Unmute Mic"))
            {
                _isMuted = false;
            }
        }
        else
        {
            if (ImGui.Button("Mute Mic"))
            {
                _isMuted = true;
            }
        }

        ImGuiHelpers.Tooltip(_isMuted ? "Unmute the microphone" : "Mute the microphone");
    }

    private bool IsAnySpeaking()
    {
        foreach (var id in orchestrator.GetActiveSessionIds())
        {
            try
            {
                var session = orchestrator.GetSession(id);
                if (session.Context.PendingTurn is not null)
                {
                    return true;
                }
            }
            catch (KeyNotFoundException)
            {
                // Session may have ended between enumeration and lookup
            }
        }

        return false;
    }

    private void StopActiveSessions()
    {
        foreach (var id in orchestrator.GetActiveSessionIds())
        {
            try
            {
                var session = orchestrator.GetSession(id);
                _ = session.StopAsync().AsTask();
            }
            catch (KeyNotFoundException)
            {
                // Session already ended
            }
        }
    }
}
