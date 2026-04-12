using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels;

/// <summary>
///     Screen Awareness panel: toggle, target window, and capture interval for the vision subsystem.
/// </summary>
public sealed class ScreenAwareness(
    IOptionsMonitor<VisionConfig> visionOptions,
    IConfigWriter configWriter
)
{
    private VisionConfig _vision;
    private bool _initialized;

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _vision = visionOptions.CurrentValue;
        _initialized = true;
    }

    public void Render()
    {
        EnsureInitialized();
        RenderInfo();
        RenderSettings();
    }

    // ── Info ─────────────────────────────────────────────────────────────────────

    private static void RenderInfo()
    {
        ImGuiHelpers.SectionHeader("Screen Awareness");

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(
            "Experimental: periodically captures a target window and sends the screenshot to the vision LLM so the AI can comment on what is on screen."
        );
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        ImGui.Spacing();
    }

    // ── Settings ─────────────────────────────────────────────────────────────────

    private void RenderSettings()
    {
        // Enable toggle
        {
            var enabled = _vision.Enabled;

            ImGuiHelpers.SettingLabel(
                "Enable",
                "Turn screen awareness on or off. Requires a vision-capable LLM endpoint."
            );

            if (ImGui.Checkbox("##VisionEnabled", ref enabled))
            {
                _vision = _vision with { Enabled = enabled };
                configWriter.Write(_vision);
            }
        }

        if (!_vision.Enabled)
            ImGui.BeginDisabled();

        // Window Title
        {
            var title = _vision.WindowTitle;

            ImGuiHelpers.SettingLabel(
                "Window Title",
                "Exact title of the window to capture (case-sensitive)."
            );

            if (ImGui.InputText("##VisionWindowTitle", ref title, 256))
            {
                _vision = _vision with { WindowTitle = title };
                configWriter.Write(_vision);
            }
        }

        // Capture Interval
        {
            var intervalSeconds = (int)_vision.CaptureInterval.TotalSeconds;

            ImGuiHelpers.SettingLabel(
                "Capture Interval",
                "How often a screenshot is sent to the vision LLM, in seconds (5–300)."
            );

            if (ImGui.SliderInt("##VisionInterval", ref intervalSeconds, 5, 300, "%d s"))
            {
                _vision = _vision with { CaptureInterval = TimeSpan.FromSeconds(intervalSeconds) };
                configWriter.Write(_vision);
            }

            ImGuiHelpers.SliderGlow();
        }

        if (!_vision.Enabled)
            ImGui.EndDisabled();
    }
}
