using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.UI.ControlPanel;
using PersonaEngine.Lib.UI.ControlPanel.Visuals;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Top status strip with audio-reactive border and persona-linked warmth.
/// </summary>
public sealed class StatusBar(IConversationOrchestrator orchestrator, PresenceOrb presenceOrb)
{
    private const int MaxSpeechSnippetLength = 80;

    // Space reserved on the left for the presence orb + gap before the label.
    private const float OrbSlotWidth = 22f;
    private const float OrbLeftPadding = 2f;
    private const float OrbLabelGap = 10f;

    private float _smoothedAmplitude;

    // Cache for the "Listening (N)" label so the multi-session case doesn't
    // interpolate a fresh string each frame. _listeningLabelCount is the N
    // that produced _cachedListeningLabel.
    private int _listeningLabelCount = -1;
    private string _cachedListeningLabel = "Listening";

    // Cache the most recently rendered "\u2014 snippet" line so repeat frames with
    // unchanged snippet text don't pay for a fresh string concatenation.
    private string? _cachedSpeechSnippet;
    private string? _cachedSpeechLine;

    public void Render(float deltaTime, PersonaStateProvider stateProvider)
    {
        presenceOrb.Update(deltaTime, stateProvider);

        var (_, label, speechText, _) = GetConversationState();

        var contentH = ImGui.GetContentRegionAvail().Y;
        var textH = ImGui.GetTextLineHeight();
        var centerY = ImGui.GetCursorPosY() + (contentH - textH) * 0.5f;

        // Reserve horizontal space for the orb — cursor moves past it so the
        // label lines up on the right of the reserved slot. Capture the orb's
        // screen-space anchor here (after row padding has been applied) so the
        // orb renders in exactly the slot we reserved.
        ImGui.SetCursorPosY(centerY);
        ImGui.Indent(OrbLeftPadding);
        var orbStartScreenX = ImGui.GetCursorScreenPos().X;
        ImGui.Dummy(new Vector2(OrbSlotWidth, textH));
        ImGui.SameLine(0f, OrbLabelGap);

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextPrimary);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();

        if (speechText is not null)
        {
            ImGui.SameLine(0f, 14f);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
            ImGui.TextUnformatted(BuildSpeechLine(GetCurrentSpeechSnippet(speechText)));
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

        // Presence orb — rendered on the foreground drawlist so the halo can
        // extend beyond the status bar bounds without being clipped to the
        // 46px child window. Position uses the screen-space anchor captured
        // earlier, which respects the row's actual padding.
        var orbDrawList = ImGui.GetForegroundDrawList();
        var orbCenter = new Vector2(
            orbStartScreenX + OrbSlotWidth * 0.5f,
            winPos.Y + winSize.Y * 0.5f
        );
        presenceOrb.Render(orbDrawList, orbCenter, stateProvider.State);
    }

    private (Vector4 Color, string Label, string? SpeechText, bool IsActive) GetConversationState()
    {
        if (!orchestrator.TryGetFirstActiveSession(out var session))
        {
            return (Theme.TextSecondary, "No Session", null, false);
        }

        var conversationState = session.CurrentState;

        if (
            conversationState
            is ConversationState.Ended
                or ConversationState.Error
                or ConversationState.Initial
        )
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

        var sessionCount = orchestrator.ActiveSessionCount;
        var label = sessionCount <= 1 ? "Listening" : GetListeningLabel(sessionCount);

        return (Theme.Success, label, null, true);
    }

    private string GetListeningLabel(int count)
    {
        if (count == _listeningLabelCount)
            return _cachedListeningLabel;

        _listeningLabelCount = count;
        _cachedListeningLabel = $"Listening ({count})";
        return _cachedListeningLabel;
    }

    private string BuildSpeechLine(string snippet)
    {
        if (
            _cachedSpeechLine is not null
            && string.Equals(_cachedSpeechSnippet, snippet, StringComparison.Ordinal)
        )
        {
            return _cachedSpeechLine;
        }

        _cachedSpeechSnippet = snippet;
        _cachedSpeechLine = "\u2014 " + snippet;
        return _cachedSpeechLine;
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
