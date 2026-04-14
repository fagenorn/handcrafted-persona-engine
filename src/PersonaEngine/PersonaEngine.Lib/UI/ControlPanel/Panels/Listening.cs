using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Audio;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Strategies;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels;

/// <summary>
///     Listening panel: microphone device selection, VAD tuning, and barge-in behaviour.
/// </summary>
public sealed class Listening(
    IOptionsMonitor<MicrophoneConfiguration> micOptions,
    IOptionsMonitor<AsrConfiguration> asrOptions,
    IOptionsMonitor<ConversationOptions> conversationOptions,
    IMicrophone microphone,
    IConfigWriter configWriter
)
{
    private static readonly string[] _bargeInLabels = Enum.GetNames<BargeInType>();
    private static readonly BargeInType[] _bargeInValues = Enum.GetValues<BargeInType>();

    private const string DefaultDeviceLabel = "(Default Device)";

    private MicrophoneConfiguration _mic;
    private AsrConfiguration _asr;
    private ConversationOptions _conversation;

    private string[] _devices = [];
    private bool _initialized;

    public void Render(float deltaTime)
    {
        EnsureInitialized();
        RenderMicrophone();
        RenderSpeechDetection();
        RenderInterruptionBehavior();
    }

    // ── Initialization ───────────────────────────────────────────────────────────

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _mic = micOptions.CurrentValue;
        _asr = asrOptions.CurrentValue;
        _conversation = conversationOptions.CurrentValue;

        RefreshDevices();
        _initialized = true;
    }

    private void RefreshDevices()
    {
        var discovered = microphone.GetAvailableDevices().ToArray();
        _devices = new string[discovered.Length + 1];
        _devices[0] = DefaultDeviceLabel;
        Array.Copy(discovered, 0, _devices, 1, discovered.Length);
    }

    // ── Microphone ───────────────────────────────────────────────────────────────

    private void RenderMicrophone()
    {
        ImGuiHelpers.SectionHeader("Microphone");

        // Determine currently selected index (0 = default)
        var currentIndex = 0;
        if (!string.IsNullOrWhiteSpace(_mic.DeviceName))
        {
            for (var i = 1; i < _devices.Length; i++)
            {
                if (string.Equals(_devices[i], _mic.DeviceName, StringComparison.Ordinal))
                {
                    currentIndex = i;
                    break;
                }
            }
        }

        ImGuiHelpers.SettingLabel(
            "Input Device",
            "The audio input device used for microphone capture."
        );

        if (ImGui.Combo("##MicDevice", ref currentIndex, _devices, _devices.Length))
        {
            var selected = currentIndex == 0 ? null : _devices[currentIndex];
            _mic = _mic with { DeviceName = selected };
            configWriter.Write(_mic);
        }

        ImGuiHelpers.HandCursorOnHover();
        ImGui.SameLine();

        if (ImGui.Button("Refresh"))
        {
            RefreshDevices();
        }

        ImGuiHelpers.HandCursorOnHover();

        ImGuiHelpers.Tooltip("Re-scan for available audio input devices.");
    }

    // ── Speech Detection ─────────────────────────────────────────────────────────

    private void RenderSpeechDetection()
    {
        ImGuiHelpers.SectionHeader("Speech Detection");

        // Voice Sensitivity (VadThreshold)
        {
            var threshold = _asr.VadThreshold;

            ImGuiHelpers.SettingLabel(
                "Voice Sensitivity",
                "VAD confidence threshold — higher values require a stronger speech signal to trigger (0.1–0.95)."
            );

            if (ImGui.SliderFloat("##VadThreshold", ref threshold, 0.1f, 0.95f, "%.2f"))
            {
                _asr = _asr with { VadThreshold = threshold };
                configWriter.Write(_asr);
            }

            ImGuiHelpers.SliderGlow();
        }

        // Silence Duration (VadMinSilenceDuration)
        {
            var silenceDuration = _asr.VadMinSilenceDuration;

            ImGuiHelpers.SettingLabel(
                "Silence Duration",
                "Minimum silence required before speech is considered ended (100–2000 ms)."
            );

            if (ImGui.SliderFloat("##VadMinSilence", ref silenceDuration, 100f, 2000f, "%.0f ms"))
            {
                _asr = _asr with { VadMinSilenceDuration = silenceDuration };
                configWriter.Write(_asr);
            }

            ImGuiHelpers.SliderGlow();
        }

        // Min Speech Duration (VadMinSpeechDuration)
        {
            var speechDuration = _asr.VadMinSpeechDuration;

            ImGuiHelpers.SettingLabel(
                "Min Speech Duration",
                "Minimum duration of detected speech before it is considered a valid utterance (50–1000 ms)."
            );

            if (ImGui.SliderFloat("##VadMinSpeech", ref speechDuration, 50f, 1000f, "%.0f ms"))
            {
                _asr = _asr with { VadMinSpeechDuration = speechDuration };
                configWriter.Write(_asr);
            }

            ImGuiHelpers.SliderGlow();
        }
    }

    // ── Interruption Behavior ────────────────────────────────────────────────────

    private void RenderInterruptionBehavior()
    {
        ImGuiHelpers.SectionHeader("Interruption Behavior");

        // Interruption Mode (BargeInType)
        {
            var currentIndex = Array.IndexOf(_bargeInValues, _conversation.BargeInType);
            if (currentIndex < 0)
                currentIndex = 0;

            ImGuiHelpers.SettingLabel(
                "Interruption Mode",
                "Controls when user speech can interrupt an in-progress response."
            );

            if (
                ImGui.Combo(
                    "##BargeInType",
                    ref currentIndex,
                    _bargeInLabels,
                    _bargeInLabels.Length
                )
            )
            {
                _conversation = _conversation with { BargeInType = _bargeInValues[currentIndex] };
                configWriter.Write(_conversation);
            }

            ImGuiHelpers.HandCursorOnHover();
        }

        // Interruption Threshold (BargeInMinWords)
        {
            var minWords = _conversation.BargeInMinWords;

            ImGuiHelpers.SettingLabel(
                "Interruption Threshold",
                "Minimum number of recognised words required to trigger an interruption (1–10)."
            );

            if (ImGui.SliderInt("##BargeInMinWords", ref minWords, 1, 10))
            {
                _conversation = _conversation with { BargeInMinWords = minWords };
                configWriter.Write(_conversation);
            }

            ImGuiHelpers.SliderGlow();
        }
    }
}
