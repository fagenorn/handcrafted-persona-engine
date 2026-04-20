using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Audition;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Models;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Sections;

/// <summary>
///     Compact summary of the current voice: display name, gender chip, engine id (muted),
///     CLONED badge when RVC is active, and a master ▶ to preview the full stack.
/// </summary>
public sealed class VoiceCard(
    IOptionsMonitor<TtsConfiguration> ttsOptions,
    IOptionsMonitor<RVCFilterOptions> rvcOptions,
    VoiceMetadataCatalog catalog,
    IVoiceAuditionService audition
)
{
    private float _elapsed;
    private const string PreviewId = "voice_card";

    public void Render(float dt, VoiceMode mode)
    {
        _elapsed += dt;
        using (Ui.Card("##voice_card", padding: 15f))
        {
            // Subtle accent tint behind the whole card
            var drawList = ImGui.GetWindowDrawList();
            var cardMin = ImGui.GetWindowPos();
            var cardMax = cardMin + ImGui.GetWindowSize();
            ImGui.AddRectFilled(
                drawList,
                cardMin,
                cardMax,
                ImGui.ColorConvertFloat4ToU32(Theme.AccentPrimary with { W = 0.06f })
            );

            var (voiceId, engine) =
                mode == VoiceMode.Clear
                    ? (ttsOptions.CurrentValue.Kokoro.DefaultVoice, VoiceEngine.Kokoro)
                    : (ttsOptions.CurrentValue.Qwen3.Speaker, VoiceEngine.Qwen3);

            var descriptor = catalog.Resolve(engine, voiceId);

            // "Selected Voice" label
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
            ImGui.TextUnformatted("Selected Voice");
            ImGui.PopStyleColor();

            // Display name + master ▶
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.AccentPrimary);
            ImGui.TextUnformatted(descriptor.DisplayName);
            ImGui.PopStyleColor();

            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 28f);
            var cardState =
                audition.ActivePreviewId == PreviewId ? ImGuiHelpers.PreviewButtonState.Playing
                : audition.IsPreviewing ? ImGuiHelpers.PreviewButtonState.Disabled
                : ImGuiHelpers.PreviewButtonState.Idle;
            if (ImGuiHelpers.PreviewButton("##preview_card", cardState, _elapsed))
            {
                if (cardState == ImGuiHelpers.PreviewButtonState.Playing)
                    _ = audition.StopAsync();
                else
                    _ = audition.PreviewAsync(BuildRequest(mode));
            }

            // Row 2: engine id (muted) + gender chip + CLONED chip
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
            ImGui.TextUnformatted(VoiceModeMapping.ToEngineId(mode));
            ImGui.PopStyleColor();

            if (descriptor.Gender is { } gender)
            {
                ImGui.SameLine();
                ImGuiHelpers.Chip(gender.ToString(), selected: false, interactive: false);
            }

            if (rvcOptions.CurrentValue.Enabled)
            {
                ImGui.SameLine();
                ImGuiHelpers.Chip("CLONED", selected: true, interactive: false);
            }

            // Row 3: description
            if (descriptor.Description is { } desc)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
                ImGui.PushTextWrapPos(0f);
                ImGui.TextUnformatted(desc);
                ImGui.PopTextWrapPos();
                ImGui.PopStyleColor();
            }
        }
    }

    private VoiceAuditionRequest BuildRequest(VoiceMode mode) =>
        mode == VoiceMode.Clear
            ? new VoiceAuditionRequest
            {
                Id = PreviewId,
                Engine = "kokoro",
                Voice = ttsOptions.CurrentValue.Kokoro.DefaultVoice,
                Speed = ttsOptions.CurrentValue.Kokoro.DefaultSpeed,
                RvcEnabled = rvcOptions.CurrentValue.Enabled,
                RvcVoice = rvcOptions.CurrentValue.DefaultVoice,
                RvcPitchShift = rvcOptions.CurrentValue.F0UpKey,
            }
            : new VoiceAuditionRequest
            {
                Id = PreviewId,
                Engine = "qwen3",
                Voice = ttsOptions.CurrentValue.Qwen3.Speaker,
                Expressiveness = ttsOptions.CurrentValue.Qwen3.Temperature,
            };
}
