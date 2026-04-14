using System.Numerics;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Sections;

/// <summary>
///     Top-of-panel selector: two cards representing Clear (Kokoro) and Expressive (Qwen3)
///     with honest trade-off copy. Clicking a card writes to
///     <see cref="TtsConfiguration.ActiveEngine" /> via <see cref="IConfigWriter" />.
/// </summary>
public sealed class VoiceModeSelector : IDisposable
{
    private const string ClearSubtitle =
        "Crisp, phoneme-accurate speech. Light on GPU. Pair with a voice clone for character.";

    private const string ExpressiveSubtitle =
        "Natural, context-aware emotion and intonation. Heavy on GPU. Stands alone.";

    private readonly IConfigWriter _configWriter;
    private readonly IDisposable? _changeSubscription;

    private TtsConfiguration _tts;

    public VoiceModeSelector(
        IOptionsMonitor<TtsConfiguration> ttsOptions,
        IConfigWriter configWriter
    )
    {
        _configWriter = configWriter;
        _tts = ttsOptions.CurrentValue;
        _changeSubscription = ttsOptions.OnChange((updated, _) => _tts = updated);
    }

    public void Dispose() => _changeSubscription?.Dispose();

    /// <summary>
    ///     The current mode, updated immediately on card click (no config round-trip).
    ///     Read by <see cref="VoicePanel" /> and passed to all sections so the UI
    ///     updates in the same frame as the click.
    /// </summary>
    public VoiceMode CurrentMode => VoiceModeMapping.FromEngineId(_tts.ActiveEngine);

    public void Render(float dt)
    {
        var activeMode = CurrentMode;
        var cardWidth = (ImGui.GetContentRegionAvail().X - 12f) * 0.5f;

        RenderModeCard(
            VoiceMode.Clear,
            "Clear",
            ClearSubtitle,
            activeMode == VoiceMode.Clear,
            cardWidth
        );
        ImGui.SameLine(0f, 12f);
        RenderModeCard(
            VoiceMode.Expressive,
            "Expressive",
            ExpressiveSubtitle,
            activeMode == VoiceMode.Expressive,
            cardWidth
        );
    }

    private void RenderModeCard(
        VoiceMode mode,
        string title,
        string subtitle,
        bool selected,
        float width
    )
    {
        using (Ui.Card($"##mode_{mode}", padding: 25f, width: width))
        {
            if (selected)
            {
                var drawList = ImGui.GetWindowDrawList();
                var min = ImGui.GetWindowPos();
                var max = min + ImGui.GetWindowSize();
                ImGui.AddRectFilled(
                    drawList,
                    min,
                    max,
                    ImGui.ColorConvertFloat4ToU32(Theme.AccentPrimary with { W = 0.08f })
                );
            }

            ImGui.PushStyleColor(ImGuiCol.Text, selected ? Theme.AccentPrimary : Theme.TextPrimary);
            ImGui.TextUnformatted(title);
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.PushTextWrapPos(0f);
            ImGui.TextUnformatted(subtitle);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();

            var cardHovered =
                !selected
                && ImGui.IsWindowHovered(ImGuiHoveredFlags.None)
                && !ImGui.IsAnyItemHovered();
            if (cardHovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }

            if (cardHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                Select(mode);
            }
        }
    }

    private void Select(VoiceMode mode)
    {
        var engineId = VoiceModeMapping.ToEngineId(mode);
        if (string.Equals(_tts.ActiveEngine, engineId, StringComparison.OrdinalIgnoreCase))
            return;
        var updated = _tts with { ActiveEngine = engineId };
        _tts = updated;
        _configWriter.Write(updated);
    }
}
