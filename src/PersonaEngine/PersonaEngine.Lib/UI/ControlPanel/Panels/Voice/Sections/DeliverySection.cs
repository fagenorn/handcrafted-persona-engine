using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Sections;

/// <summary>
///     Delivery controls: Pace (Kokoro speed) and Expressiveness (Qwen3 temperature — only
///     shown in Expressive mode). Writes each change through <see cref="IConfigWriter" /> on
///     the specific options type so the writer's type→path map routes to the correct JSON
///     section. Pitch lives in <see cref="CloneLayerSection" /> because it is the RVC semitone
///     shift — semantically part of the clone layer, not base delivery.
/// </summary>
/// <remarks>
///     Options are cached locally so that slider/toggle changes are reflected immediately on
///     the next frame without waiting for the debounced config file write and options-monitor
///     reload cycle (which caused visible snap-back on every drag frame).
/// </remarks>
public sealed class DeliverySection : IDisposable
{
    private readonly IOptionsMonitor<TtsConfiguration> _ttsOptions;
    private readonly IConfigWriter _configWriter;

    private KokoroVoiceOptions _kokoro;
    private Qwen3TtsOptions _qwen3;
    private readonly IDisposable? _changeSubscription;

    public DeliverySection(IOptionsMonitor<TtsConfiguration> ttsOptions, IConfigWriter configWriter)
    {
        _ttsOptions = ttsOptions;
        _configWriter = configWriter;

        var current = ttsOptions.CurrentValue;
        _kokoro = current.Kokoro;
        _qwen3 = current.Qwen3;

        // Refresh the local cache when the config file changes externally (e.g. manual edit).
        _changeSubscription = ttsOptions.OnChange(
            (updated, _) =>
            {
                _kokoro = updated.Kokoro;
                _qwen3 = updated.Qwen3;
            }
        );
    }

    public void Dispose() => _changeSubscription?.Dispose();

    public void Render(float dt, VoiceMode mode)
    {
        ImGuiHelpers.SectionHeader("Delivery");

        float rowY;

        // Pace
        rowY = Hexa.NET.ImGui.ImGui.GetCursorPosY();
        var speed = _kokoro.DefaultSpeed;
        if (ImGuiHelpers.LabeledSlider("##pace", ref speed, 0.5f, 2.0f, "Slow", "Fast", "%.2f", dt))
        {
            _kokoro = _kokoro with { DefaultSpeed = speed };
            _configWriter.Write(_kokoro);
        }
        ImGuiHelpers.SettingEndRow(rowY);

        // Expressiveness — only visible in Expressive mode
        if (mode == VoiceMode.Expressive)
        {
            rowY = Hexa.NET.ImGui.ImGui.GetCursorPosY();
            var temperature = _qwen3.Temperature;
            if (
                ImGuiHelpers.LabeledSlider(
                    "##expressiveness",
                    ref temperature,
                    0.1f,
                    1.5f,
                    "Flat",
                    "Theatrical",
                    "%.2f",
                    dt
                )
            )
            {
                _qwen3 = _qwen3 with { Temperature = temperature };
                _configWriter.Write(_qwen3);
            }
            ImGuiHelpers.SettingEndRow(rowY);
        }
    }
}
