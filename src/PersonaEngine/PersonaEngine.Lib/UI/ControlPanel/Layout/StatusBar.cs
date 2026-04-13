using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.UI.ControlPanel;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Top status strip with audio-reactive border and persona-linked warmth.
/// </summary>
public sealed class StatusBar(IConversationOrchestrator orchestrator)
{
    private const int MaxSpeechSnippetLength = 80;

    private float _elapsed;

    // Smooth the amplitude for the border glow
    private float _smoothedAmplitude;

    // Dot flare on state transitions
    private OneShotAnimation _dotFlare;

    public void Render(float deltaTime, PersonaStateProvider stateProvider)
    {
        _elapsed += deltaTime;
        _dotFlare.Update(deltaTime);

        var (color, label, speechText, isActive) = GetConversationState();

        // Trigger dot flare on persona state transition
        if (stateProvider.StateJustChanged)
            _dotFlare.Start(0.2f);

        // Base pulse: oscillates between 0.08 and 0.20 opacity at 2.5 Hz, disabled when inactive
        var glowAlpha = isActive ? 0.14f + 0.06f * MathF.Sin(_elapsed * 2.5f) : 0f;

        // Flare boost
        if (_dotFlare.IsActive)
        {
            var flareT = Easing.EaseOutCubic(_dotFlare.Progress);
            glowAlpha += (1f - flareT) * 0.15f;
        }

        var contentH = ImGui.GetContentRegionAvail().Y;
        var textH = ImGui.GetTextLineHeight();
        var centerY = ImGui.GetCursorPosY() + (contentH - textH) * 0.5f;

        ImGui.SetCursorPosY(centerY);

        ImGuiHelpers.StatusDot(color, glowAlpha: glowAlpha);
        ImGui.SameLine(0f, 10f);

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

        // Persona-linked warmth overlay
        var drawList = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();

        var warmthColor = stateProvider.State switch
        {
            PersonaUiState.Speaking => Theme.AccentPrimary with { W = 0.10f },
            PersonaUiState.Thinking => Theme.AccentSecondary with { W = 0.07f },
            _ => Vector4.Zero,
        };

        if (warmthColor.W > 0f)
        {
            var warmthCol = ImGui.ColorConvertFloat4ToU32(warmthColor);
            ImGui.AddRectFilled(drawList, winPos, winPos + winSize, warmthCol);
        }

        // Audio-reactive bottom accent border
        var targetAmplitude = stateProvider.IsAudioPlaying ? stateProvider.AudioAmplitude : 0f;
        _smoothedAmplitude +=
            (targetAmplitude - _smoothedAmplitude) * (1f - MathF.Exp(-12f * deltaTime));

        var borderAlpha = stateProvider.IsAudioPlaying ? 0.15f + _smoothedAmplitude * 0.30f : 0.25f;

        var borderY = winPos.Y + winSize.Y - 1f;
        var borderColor = ImGui.ColorConvertFloat4ToU32(
            Theme.AccentPrimary with
            {
                W = borderAlpha,
            }
        );
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

        IConversationSession? session = null;
        try
        {
            session = orchestrator.GetSession(sessionIds[0]);
        }
        catch (KeyNotFoundException)
        {
            return (Theme.TextSecondary, "No Session", null, false);
        }

        var pendingTurn = session.Context.PendingTurn;

        if (pendingTurn is not null)
        {
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
