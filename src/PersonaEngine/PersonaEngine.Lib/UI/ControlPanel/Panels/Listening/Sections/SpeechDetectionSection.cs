using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Listening.Sections;

/// <summary>
///     Speech detection card: hosts the live meter and the three VAD sliders (sensitivity,
///     silence duration, minimum utterance). The meter's threshold line follows the
///     sensitivity slider in real time.
/// </summary>
public sealed class SpeechDetectionSection : IDisposable
{
    private readonly IConfigWriter _configWriter;
    private readonly LiveMeterWidget _meter;
    private readonly IDisposable? _changeSubscription;

    private AsrConfiguration _asr;
    private bool _thresholdSliderActive;

    public SpeechDetectionSection(
        IOptionsMonitor<AsrConfiguration> monitor,
        LiveMeterWidget meter,
        IConfigWriter configWriter
    )
    {
        _meter = meter;
        _configWriter = configWriter;
        _asr = monitor.CurrentValue;
        _changeSubscription = monitor.OnChange((updated, _) => _asr = updated);
    }

    public void Dispose() => _changeSubscription?.Dispose();

    public void Render(float dt)
    {
        using (Ui.Card("##speech_detection", padding: 12f))
        {
            RenderHeader();

            // Meter first, sliders below. Because ImGui.IsItemActive reads the PREVIOUS
            // widget's state and we want the threshold line to light up while dragging
            // the sensitivity slider (which is drawn below the meter), we use a
            // one-frame lag: pass last frame's active flag to the meter, capture the
            // new value after the slider is drawn, use it next frame.
            _meter.Render(dt, _asr.VadThreshold, _thresholdSliderActive);

            ImGui.Spacing();
            RenderSensitivitySlider(dt);
            RenderSilenceSlider(dt);
            RenderMinSpeechSlider(dt);
        }
    }

    private static void RenderHeader()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
        ImGui.TextUnformatted("Speech Detection");
        ImGui.PopStyleColor();

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted("When we think you've started — and finished — speaking");
        ImGui.PopStyleColor();

        ImGui.Spacing();
    }

    private void RenderSensitivitySlider(float dt)
    {
        var rowY = ImGui.GetCursorPosY();
        var threshold = _asr.VadThreshold;

        ImGuiHelpers.SettingLabel(
            "Voice Sensitivity",
            "Higher values require a clearer speech signal. Watch the probability line in the meter — the threshold should sit just above your room's background noise."
        );

        if (
            ImGuiHelpers.LabeledSlider(
                "##sens",
                ref threshold,
                0.1f,
                0.95f,
                "Quiet",
                "Loud",
                "%.2f",
                dt
            )
        )
        {
            _asr = _asr with { VadThreshold = threshold };
            _configWriter.Write(_asr);
        }

        // Capture the slider's active state for next frame's meter rendering.
        _thresholdSliderActive = ImGui.IsItemActive();

        ImGuiHelpers.SettingEndRow(rowY);
    }

    private void RenderSilenceSlider(float dt)
    {
        var rowY = ImGui.GetCursorPosY();
        var ms = _asr.VadMinSilenceDuration;

        ImGuiHelpers.SettingLabel(
            "Pause Before Done",
            "How long you need to be quiet before we consider you finished. Lower = avatar jumps in sooner; higher = you can pause mid-sentence."
        );

        if (
            ImGuiHelpers.LabeledSlider(
                "##pause",
                ref ms,
                100f,
                2000f,
                "Snappy",
                "Patient",
                "%.0f ms",
                dt
            )
        )
        {
            _asr = _asr with { VadMinSilenceDuration = ms };
            _configWriter.Write(_asr);
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }

    private void RenderMinSpeechSlider(float dt)
    {
        var rowY = ImGui.GetCursorPosY();
        var ms = _asr.VadMinSpeechDuration;

        ImGuiHelpers.SettingLabel(
            "Minimum Utterance",
            "How much speech we need before counting it. Raise this to ignore short coughs or keyboard taps."
        );

        if (
            ImGuiHelpers.LabeledSlider(
                "##minspeech",
                ref ms,
                50f,
                1000f,
                "Twitchy",
                "Deliberate",
                "%.0f ms",
                dt
            )
        )
        {
            _asr = _asr with { VadMinSpeechDuration = ms };
            _configWriter.Write(_asr);
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }
}
