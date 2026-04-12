using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.TTS.RVC;
using PersonaEngine.Lib.TTS.Synthesis.Kokoro;
using PersonaEngine.Lib.TTS.Synthesis.Qwen3;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels;

/// <summary>
///     Voice panel: TTS engine selection, voice settings, and voice cloning (RVC).
/// </summary>
public sealed class Voice(
    IOptionsMonitor<TtsConfiguration> ttsOptions,
    IOptionsMonitor<KokoroVoiceOptions> kokoroOptions,
    IOptionsMonitor<Qwen3TtsOptions> qwen3Options,
    IOptionsMonitor<RVCFilterOptions> rvcOptions,
    IKokoroVoiceProvider kokoroVoiceProvider,
    IQwen3VoiceProvider qwen3VoiceProvider,
    IRVCVoiceProvider rvcVoiceProvider,
    IConfigWriter configWriter
)
{
    private static readonly string[] _engineLabels = ["Kokoro", "Qwen3"];
    private static readonly string[] _engineIds = ["kokoro", "qwen3"];

    private TtsConfiguration _tts;
    private KokoroVoiceOptions _kokoro;
    private Qwen3TtsOptions _qwen3;
    private RVCFilterOptions _rvc;

    private string[] _kokoroVoices = [];
    private string[] _qwen3Speakers = [];
    private string[] _rvcVoices = [];
    private bool _initialized;

    private AnimatedFloat _britishEnglishKnob;
    private AnimatedFloat _trimSilenceKnob;
    private AnimatedFloat _rvcEnabledKnob;

    public void Render(float deltaTime)
    {
        EnsureInitialized();
        RenderEngineSelector();
        RenderEngineSettings(deltaTime);
        RenderVoiceCloning(deltaTime);
    }

    // ── Initialization ───────────────────────────────────────────────────────────

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _tts = ttsOptions.CurrentValue;
        _kokoro = kokoroOptions.CurrentValue;
        _qwen3 = qwen3Options.CurrentValue;
        _rvc = rvcOptions.CurrentValue;

        _kokoroVoices = kokoroVoiceProvider.GetAvailableVoices().ToArray();
        _qwen3Speakers = qwen3VoiceProvider.GetAvailableSpeakers().ToArray();
        _rvcVoices = rvcVoiceProvider.GetAvailableVoices().ToArray();

        _britishEnglishKnob = new AnimatedFloat(_kokoro.UseBritishEnglish ? 1f : 0f);
        _trimSilenceKnob = new AnimatedFloat(_kokoro.TrimSilence ? 1f : 0f);
        _rvcEnabledKnob = new AnimatedFloat(_rvc.Enabled ? 1f : 0f);

        _initialized = true;
    }

    // ── Engine selector ──────────────────────────────────────────────────────────

    private void RenderEngineSelector()
    {
        ImGuiHelpers.SectionHeader("Voice Engine");

        var currentIndex = Array.IndexOf(_engineIds, _tts.ActiveEngine);
        if (currentIndex < 0)
            currentIndex = 0;

        ImGuiHelpers.SettingLabel("Engine", "The TTS engine used to synthesise speech.");

        if (ImGui.Combo("##Engine", ref currentIndex, _engineLabels, _engineLabels.Length))
        {
            _tts = _tts with { ActiveEngine = _engineIds[currentIndex] };
            configWriter.Write(_tts);
        }
    }

    // ── Engine settings dispatcher ───────────────────────────────────────────────

    private void RenderEngineSettings(float dt)
    {
        if (string.Equals(_tts.ActiveEngine, "kokoro", StringComparison.OrdinalIgnoreCase))
        {
            RenderKokoroSettings(dt);
        }
        else if (string.Equals(_tts.ActiveEngine, "qwen3", StringComparison.OrdinalIgnoreCase))
        {
            RenderQwen3Settings();
        }
    }

    // ── Kokoro settings ──────────────────────────────────────────────────────────

    private void RenderKokoroSettings(float dt)
    {
        ImGuiHelpers.SectionHeader("Kokoro Settings");

        // Voice dropdown
        {
            var currentIndex = Array.IndexOf(_kokoroVoices, _kokoro.DefaultVoice);
            if (currentIndex < 0)
                currentIndex = 0;

            ImGuiHelpers.SettingLabel("Voice", "The Kokoro voice embedding to use.");

            if (ImGui.Combo("##KokoroVoice", ref currentIndex, _kokoroVoices, _kokoroVoices.Length))
            {
                var selected =
                    _kokoroVoices.Length > 0 ? _kokoroVoices[currentIndex] : _kokoro.DefaultVoice;
                _kokoro = _kokoro with { DefaultVoice = selected };
                configWriter.Write(_kokoro);
            }
        }

        // Speaking Speed
        {
            var speed = _kokoro.DefaultSpeed;

            ImGuiHelpers.SettingLabel(
                "Speaking Speed",
                "Playback speed multiplier (1.0 = normal)."
            );

            if (ImGui.SliderFloat("##Speed", ref speed, 0.5f, 2.0f, "%.1f"))
            {
                _kokoro = _kokoro with { DefaultSpeed = speed };
                configWriter.Write(_kokoro);
            }

            ImGuiHelpers.SliderGlow();
        }

        // British English
        {
            var british = _kokoro.UseBritishEnglish;

            ImGuiHelpers.SettingLabel("British English", "Use British English phoneme variants.");

            if (
                ImGuiHelpers.ToggleSwitch(
                    "##BritishEnglish",
                    ref british,
                    ref _britishEnglishKnob,
                    dt
                )
            )
            {
                _kokoro = _kokoro with { UseBritishEnglish = british };
                configWriter.Write(_kokoro);
            }
        }

        // Trim Silence
        {
            var trim = _kokoro.TrimSilence;

            ImGuiHelpers.SettingLabel(
                "Trim Silence",
                "Strip leading and trailing silence from each audio segment."
            );

            if (ImGuiHelpers.ToggleSwitch("##TrimSilence", ref trim, ref _trimSilenceKnob, dt))
            {
                _kokoro = _kokoro with { TrimSilence = trim };
                configWriter.Write(_kokoro);
            }
        }

        // Max Segment Length
        {
            var maxLen = _kokoro.MaxPhonemeLength;

            ImGuiHelpers.SettingLabel(
                "Max Segment Length",
                "Maximum phoneme length per synthesis segment (100–510)."
            );

            if (ImGui.SliderInt("##MaxSegLen", ref maxLen, 100, 510))
            {
                _kokoro = _kokoro with { MaxPhonemeLength = maxLen };
                configWriter.Write(_kokoro);
            }

            ImGuiHelpers.SliderGlow();
        }
    }

    // ── Qwen3 settings ───────────────────────────────────────────────────────────

    private void RenderQwen3Settings()
    {
        ImGuiHelpers.SectionHeader("Qwen3 Settings");

        // Speaker dropdown
        {
            var currentIndex = Array.IndexOf(_qwen3Speakers, _qwen3.Speaker);
            if (currentIndex < 0)
                currentIndex = 0;

            ImGuiHelpers.SettingLabel("Speaker", "The Qwen3-TTS speaker voice.");

            if (
                ImGui.Combo(
                    "##Qwen3Speaker",
                    ref currentIndex,
                    _qwen3Speakers,
                    _qwen3Speakers.Length
                )
            )
            {
                var selected =
                    _qwen3Speakers.Length > 0 ? _qwen3Speakers[currentIndex] : _qwen3.Speaker;
                _qwen3 = _qwen3 with { Speaker = selected };
                configWriter.Write(_qwen3);
            }
        }

        // Expressiveness (Temperature)
        {
            var temperature = _qwen3.Temperature;

            ImGuiHelpers.SettingLabel(
                "Expressiveness",
                "Sampling temperature — higher values produce more expressive output (0.1–1.5)."
            );

            if (ImGui.SliderFloat("##Temperature", ref temperature, 0.1f, 1.5f, "%.2f"))
            {
                _qwen3 = _qwen3 with { Temperature = temperature };
                configWriter.Write(_qwen3);
            }

            ImGuiHelpers.SliderGlow();
        }

        // Repetition Penalty
        {
            var repPenalty = _qwen3.RepetitionPenalty;

            ImGuiHelpers.SettingLabel(
                "Repetition Penalty",
                "Penalises repeated tokens to reduce stuttering artefacts (1.0–1.5)."
            );

            if (ImGui.SliderFloat("##RepPenalty", ref repPenalty, 1.0f, 1.5f, "%.2f"))
            {
                _qwen3 = _qwen3 with { RepetitionPenalty = repPenalty };
                configWriter.Write(_qwen3);
            }

            ImGuiHelpers.SliderGlow();
        }

        // Max Length
        {
            var maxTokens = _qwen3.MaxNewTokens;

            ImGuiHelpers.SettingLabel(
                "Max Length",
                "Maximum number of new tokens to generate per utterance (256–4096)."
            );

            if (ImGui.SliderInt("##MaxTokens", ref maxTokens, 256, 4096))
            {
                _qwen3 = _qwen3 with { MaxNewTokens = maxTokens };
                configWriter.Write(_qwen3);
            }

            ImGuiHelpers.SliderGlow();
        }
    }

    // ── Voice cloning (RVC) ──────────────────────────────────────────────────────

    private void RenderVoiceCloning(float dt)
    {
        ImGuiHelpers.SectionHeader("Voice Cloning (RVC)");

        // Enable toggle
        {
            var enabled = _rvc.Enabled;

            ImGuiHelpers.SettingLabel("Enable", "Apply RVC voice conversion to synthesised audio.");

            if (ImGuiHelpers.ToggleSwitch("##RvcEnabled", ref enabled, ref _rvcEnabledKnob, dt))
            {
                _rvc = _rvc with { Enabled = enabled };
                configWriter.Write(_rvc);
            }
        }

        if (!_rvc.Enabled)
            ImGui.BeginDisabled();

        // Clone Voice dropdown
        {
            var currentIndex = Array.IndexOf(_rvcVoices, _rvc.DefaultVoice);
            if (currentIndex < 0)
                currentIndex = 0;

            ImGuiHelpers.SettingLabel(
                "Clone Voice",
                "The RVC model to apply for voice conversion."
            );

            if (ImGui.Combo("##RvcVoice", ref currentIndex, _rvcVoices, _rvcVoices.Length))
            {
                var selected = _rvcVoices.Length > 0 ? _rvcVoices[currentIndex] : _rvc.DefaultVoice;
                _rvc = _rvc with { DefaultVoice = selected };
                configWriter.Write(_rvc);
            }
        }

        // Pitch Shift (F0UpKey)
        {
            var f0UpKey = _rvc.F0UpKey;

            ImGuiHelpers.SettingLabel(
                "Pitch Shift",
                "Semitone shift applied to the fundamental frequency (-12–12)."
            );

            if (ImGui.SliderInt("##PitchShift", ref f0UpKey, -12, 12))
            {
                _rvc = _rvc with { F0UpKey = f0UpKey };
                configWriter.Write(_rvc);
            }

            ImGuiHelpers.SliderGlow();
        }

        // Processing Quality (HopSize)
        {
            var hopSize = _rvc.HopSize;

            ImGuiHelpers.SettingLabel(
                "Processing Quality",
                "Hop size for RVC inference — smaller values are higher quality but slower (32–256)."
            );

            if (ImGui.SliderInt("##HopSize", ref hopSize, 32, 256))
            {
                _rvc = _rvc with { HopSize = hopSize };
                configWriter.Write(_rvc);
            }

            ImGuiHelpers.SliderGlow();
        }

        if (!_rvc.Enabled)
            ImGui.EndDisabled();
    }
}
