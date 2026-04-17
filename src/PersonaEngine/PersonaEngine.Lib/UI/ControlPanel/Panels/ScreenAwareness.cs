using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels;

/// <summary>
///     Screen Awareness panel: toggle, target window, and capture interval for the vision subsystem.
/// </summary>
public sealed class ScreenAwareness(
    IOptionsMonitor<VisionConfig> visionOptions,
    IOptionsMonitor<LlmOptions> llmOptions,
    IConfigWriter configWriter
)
{
    private VisionConfig _vision;
    private LlmOptions _llm;
    private bool _initialized;

    private AnimatedFloat _enabledKnob;

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _vision = visionOptions.CurrentValue;
        _llm = llmOptions.CurrentValue;
        _enabledKnob = new AnimatedFloat(_llm.VisionEnabled ? 1f : 0f);
        _initialized = true;
    }

    public void Render(float deltaTime)
    {
        EnsureInitialized();
        RenderInfo();
        RenderSettings(deltaTime);
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

    private void RenderSettings(float dt)
    {
        // Enable toggle
        {
            var enabled = _llm.VisionEnabled;

            ImGuiHelpers.SettingLabel(
                "Enable",
                "Turn screen awareness on or off. Requires a vision-capable LLM endpoint."
            );

            if (ImGuiHelpers.ToggleSwitch("##VisionEnabled", ref enabled, ref _enabledKnob, dt))
            {
                _llm = _llm with { VisionEnabled = enabled };
                configWriter.Write(_llm);
            }
        }

        if (!_llm.VisionEnabled)
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

        if (!_llm.VisionEnabled)
            ImGui.EndDisabled();
    }
}
