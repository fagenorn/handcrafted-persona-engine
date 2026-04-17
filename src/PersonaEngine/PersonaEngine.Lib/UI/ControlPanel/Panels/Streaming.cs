using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels;

/// <summary>
///     Streaming panel: Spout output configuration for OBS integration. The
///     floating in-app overlay has its own dedicated panel
///     (<see cref="OverlayPanel" />).
/// </summary>
public sealed class Streaming(
    IOptionsMonitor<AvatarAppConfig> appOptions,
    IConfigWriter configWriter
)
{
    public void Render(float deltaTime)
    {
        // Read fresh each frame so external changes (e.g. the overlay persisting
        // its dragged position) are reflected without having to re-enter the panel.
        var cfg = appOptions.CurrentValue;

        RenderSpoutOutputs(cfg);
    }

    // ── Spout Outputs ────────────────────────────────────────────────────────────

    private void RenderSpoutOutputs(AvatarAppConfig cfg)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(
            "Spout sends rendered frames to other applications on the same machine. "
                + "Each output below corresponds to a named Spout sender that OBS or other "
                + "compatible software can capture as a video source."
        );
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        var spoutConfigs = cfg.SpoutConfigs;

        if (spoutConfigs.Length == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted("No Spout outputs configured.");
            ImGui.PopStyleColor();

            return;
        }

        for (var i = 0; i < spoutConfigs.Length; i++)
        {
            RenderSpoutOutputEntry(cfg, i);
        }
    }

    private void RenderSpoutOutputEntry(AvatarAppConfig cfg, int index)
    {
        var spoutConfigs = cfg.SpoutConfigs;
        var config = spoutConfigs[index];

        ImGuiHelpers.SectionHeader(config.OutputName);

        // Enabled toggle — turning off stops publishing to external receivers but
        // keeps the in-process frame source available for the floating overlay.
        var enabled = config.Enabled;
        ImGuiHelpers.SettingLabel(
            "Enabled",
            "When off, the Spout sender is released and external apps (OBS etc.) lose this source. "
                + "The floating overlay inside this app is unaffected."
        );
        if (ImGui.Checkbox($"##SpoutEnabled_{index}", ref enabled))
        {
            var updated = config with { Enabled = enabled };
            var newConfigs = ReplaceAt(spoutConfigs, index, updated);
            configWriter.Write(cfg with { SpoutConfigs = newConfigs });
        }

        var w = config.Width;
        var h = config.Height;

        ImGuiHelpers.SettingLabel("Resolution", "Output resolution for this Spout sender.");

        if (ImGuiHelpers.ResolutionPicker($"SpoutRes_{index}", ref w, ref h))
        {
            var updated = config with { Width = w, Height = h };
            var newConfigs = ReplaceAt(spoutConfigs, index, updated);
            configWriter.Write(cfg with { SpoutConfigs = newConfigs });
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static T[] ReplaceAt<T>(T[] source, int index, T replacement)
    {
        var result = new T[source.Length];
        source.CopyTo(result, 0);
        result[index] = replacement;

        return result;
    }
}
