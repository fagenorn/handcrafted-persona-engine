using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Assets;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels;

/// <summary>
///     Screen Awareness panel: target window and capture interval for the vision
///     subsystem. The on/off flag lives on <see cref="LlmOptions.VisionEnabled" />
///     and is set from the LLM Connection panel; when vision is disabled this
///     panel shows a hint + navigation button instead of disabled inputs.
/// </summary>
public sealed class ScreenAwareness : IDisposable
{
    private readonly IConfigWriter _configWriter;

    private readonly IAssetCatalog _catalog;

    private readonly IDisposable? _llmSub;

    private readonly INavRequestBus _navBus;

    private readonly IDisposable? _visionSub;

    private LlmOptions _llm;

    private VisionConfig _vision;

    public ScreenAwareness(
        IOptionsMonitor<VisionConfig> visionOptions,
        IOptionsMonitor<LlmOptions> llmOptions,
        IConfigWriter configWriter,
        INavRequestBus navBus,
        IAssetCatalog catalog
    )
    {
        _configWriter = configWriter;
        _navBus = navBus;
        _catalog = catalog;
        _vision = visionOptions.CurrentValue;
        _llm = llmOptions.CurrentValue;

        _visionSub = visionOptions.OnChange(OnVisionChanged);
        _llmSub = llmOptions.OnChange(OnLlmChanged);
    }

    public void Dispose()
    {
        _visionSub?.Dispose();
        _llmSub?.Dispose();
    }

    public void Render(float deltaTime)
    {
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
        // Vision capture (Rust window-grab) and the vision LLM both ship in the
        // BuildWithIt bundle. If the user is on a smaller profile, the toggle
        // would silently no-op — show a locked notice instead so they know how
        // to unlock it (and skip the LLM-enabled hint, which is irrelevant here).
        if (!_catalog.IsFeatureEnabled(FeatureIds.VisionCapture))
        {
            ImGuiHelpers.LockedSection(
                "Screen awareness",
                FeatureProfileMap.MinimumProfileLabel(FeatureIds.VisionCapture)
            );
            return;
        }

        if (!_llm.VisionEnabled)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.PushTextWrapPos(0f);
            ImGui.TextUnformatted(
                "Enable Vision LLM in the LLM Connection panel to activate screen awareness."
            );
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();

            ImGui.Spacing();

            if (ImGuiHelpers.SubtleButton("Open LLM Connection##ScreenAwareOpenLlm"))
            {
                _navBus.Request(NavSection.LlmConnection);
            }

            return;
        }

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
                _configWriter.Write(_vision);
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
                _configWriter.Write(_vision);
            }

            ImGuiHelpers.SliderGlow();
        }
    }

    private void OnVisionChanged(VisionConfig updated, string? _)
    {
        _vision = updated;
    }

    private void OnLlmChanged(LlmOptions updated, string? _)
    {
        _llm = updated;
    }
}
