using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels;

/// <summary>
///     Streaming panel: Spout output configuration for OBS integration and the
///     floating in-app overlay that mirrors the avatar without any window chrome.
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
        RenderOverlaySection(cfg);
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

    // ── Floating Overlay ─────────────────────────────────────────────────────────

    private void RenderOverlaySection(AvatarAppConfig cfg)
    {
        ImGui.Dummy(new System.Numerics.Vector2(0f, 8f));
        ImGuiHelpers.SectionHeader("Floating Overlay");

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(
            "An always-on-top transparent window that mirrors the avatar and subtitles "
                + "directly on your desktop — no OBS or window chrome. Hover it to reveal a "
                + "thin border with grab handles for moving and resizing."
        );
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        var overlay = cfg.Overlay;

        var enabled = overlay.Enabled;
        ImGuiHelpers.SettingLabel(
            "Enabled",
            "Show the floating overlay. If the overlay is force-closed during a session, "
                + "it will not reappear until the next app launch."
        );
        if (ImGui.Checkbox("##OverlayEnabled", ref enabled))
        {
            var updated = overlay with { Enabled = enabled };
            configWriter.Write(cfg with { Overlay = updated });
        }

        // Source picker — lists available Spout targets.
        ImGuiHelpers.SettingLabel(
            "Source",
            "Which rendered target to mirror. Must match one of the Spout outputs above."
        );
        var sources = cfg.SpoutConfigs;
        if (sources.Length > 0)
        {
            var currentIdx = Array.FindIndex(sources, s => s.OutputName == overlay.Source);
            if (currentIdx < 0)
            {
                currentIdx = 0;
            }

            if (ImGui.BeginCombo("##OverlaySource", sources[currentIdx].OutputName))
            {
                for (var i = 0; i < sources.Length; i++)
                {
                    var isSelected = i == currentIdx;
                    if (ImGui.Selectable(sources[i].OutputName, isSelected))
                    {
                        var updated = overlay with { Source = sources[i].OutputName };
                        configWriter.Write(cfg with { Overlay = updated });
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }
        }
        else
        {
            ImGui.TextUnformatted("(no Spout outputs configured)");
        }

        var lockAspect = overlay.LockAspect;
        ImGuiHelpers.SettingLabel(
            "Lock aspect",
            "Keep the overlay's width/height ratio matching the source resolution while resizing."
        );
        if (ImGui.Checkbox("##OverlayLockAspect", ref lockAspect))
        {
            configWriter.Write(cfg with { Overlay = overlay with { LockAspect = lockAspect } });
        }

        var fade = overlay.ChromeFadeSeconds;
        ImGuiHelpers.SettingLabel(
            "Chrome fade",
            "How quickly the hover border fades in and out (seconds)."
        );
        if (ImGui.SliderFloat("##OverlayFade", ref fade, 0.02f, 0.5f, "%.2f s"))
        {
            configWriter.Write(cfg with { Overlay = overlay with { ChromeFadeSeconds = fade } });
        }

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted(
            $"Position: {overlay.X}, {overlay.Y}    Size: {overlay.Width} x {overlay.Height}"
        );
        ImGui.TextUnformatted("(drag the overlay's border to reposition or resize)");
        ImGui.PopStyleColor();
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
