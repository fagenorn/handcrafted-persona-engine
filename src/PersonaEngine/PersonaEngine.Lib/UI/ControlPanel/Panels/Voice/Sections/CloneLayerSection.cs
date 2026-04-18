using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.TTS.RVC;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Audition;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Sections;

/// <summary>
///     RVC voice-cloning controls. Mode-sensitive: expanded + encouraged in Clear mode,
///     collapsed + de-emphasized in Expressive mode (Qwen3 handles emotion natively).
///     Provides an A/B preview that plays the sample dry then wet for side-by-side compare.
/// </summary>
/// <remarks>
///     RVC options are cached locally so that toggle/slider changes are reflected immediately
///     on the next frame without waiting for the debounced config write + options-monitor cycle.
/// </remarks>
public sealed class CloneLayerSection : IDisposable
{
    private readonly IOptionsMonitor<TtsConfiguration> _ttsOptions;
    private readonly IRVCVoiceProvider _rvcVoiceProvider;
    private readonly IVoiceAuditionService _audition;
    private readonly IConfigWriter _configWriter;

    private RVCFilterOptions _rvc;
    private readonly IDisposable? _changeSubscription;

    private AnimatedFloat _enabledKnob;
    private bool _knobInitialized;
    private float _elapsed;
    private readonly ImGuiHelpers.CollapsibleState _collapseState = new();

    // Per-frame arguments + closure for the body renderer. Cached as instance
    // fields so CollapsibleSection's Action argument reuses the same delegate
    // instance across frames instead of allocating a new closure each render.
    private float _bodyDt;
    private VoiceMode _bodyMode;
    private readonly Action _renderBodyAction;

    // RVC voice list cached so the voice picker doesn't re-array the provider
    // every frame. Rebuilt when the provider's list size changes so
    // newly-added voices still surface.
    private string[] _rvcVoicesCache = [];

    public CloneLayerSection(
        IOptionsMonitor<TtsConfiguration> ttsOptions,
        IOptionsMonitor<RVCFilterOptions> rvcOptions,
        IRVCVoiceProvider rvcVoiceProvider,
        IVoiceAuditionService audition,
        IConfigWriter configWriter
    )
    {
        _ttsOptions = ttsOptions;
        _rvcVoiceProvider = rvcVoiceProvider;
        _audition = audition;
        _configWriter = configWriter;

        _rvc = rvcOptions.CurrentValue;
        _renderBodyAction = () => RenderBody(_bodyDt, _bodyMode);

        _changeSubscription = rvcOptions.OnChange((updated, _) => _rvc = updated);
    }

    public void Dispose() => _changeSubscription?.Dispose();

    public void Render(float dt, VoiceMode mode)
    {
        _elapsed += dt;
        if (!_knobInitialized)
        {
            _enabledKnob = new AnimatedFloat(_rvc.Enabled ? 1f : 0f);
            _knobInitialized = true;
        }
        var defaultOpen = false;
        var hint =
            mode == VoiceMode.Clear
                ? "Recommended \u2014 gives Kokoro character."
                : "Rarely needed \u2014 Qwen3 reads emotion from context.";

        _bodyDt = dt;
        _bodyMode = mode;

        ImGuiHelpers.CollapsibleSection(
            "Clone Voice",
            subtitle: null,
            defaultOpen,
            _renderBodyAction,
            hint: hint,
            animState: _collapseState,
            dt: dt
        );
    }

    private void RenderBody(float dt, VoiceMode mode)
    {
        float rowY;

        // Enable toggle
        rowY = ImGui.GetCursorPosY();
        var enabled = _rvc.Enabled;
        ImGuiHelpers.SettingLabel("Enable", "Apply voice cloning (RVC) to synthesised audio.");
        if (ImGuiHelpers.ToggleSwitch("##rvc_enabled", ref enabled, ref _enabledKnob, dt))
        {
            _rvc = _rvc with { Enabled = enabled };
            _configWriter.Write(_rvc);
        }
        ImGuiHelpers.SettingEndRow(rowY);

        // Downstream controls are only meaningful when enabled — show always, but disable when off.
        if (!enabled)
            ImGui.BeginDisabled();

        // Voice picker
        rowY = ImGui.GetCursorPosY();
        var rvcVoices = GetRvcVoices();
        if (rvcVoices.Length > 0)
        {
            var current = _rvc.DefaultVoice;
            var currentIndex = Math.Max(0, Array.IndexOf(rvcVoices, current));

            ImGuiHelpers.SettingLabel("Voice", "Which RVC model to apply.");
            if (ImGui.Combo("##rvc_voice", ref currentIndex, rvcVoices, rvcVoices.Length))
            {
                _rvc = _rvc with { DefaultVoice = rvcVoices[currentIndex] };
                _configWriter.Write(_rvc);
            }

            ImGuiHelpers.HandCursorOnHover();
        }
        ImGuiHelpers.SettingEndRow(rowY);

        // Pitch shift
        rowY = ImGui.GetCursorPosY();
        var pitch = _rvc.F0UpKey;
        ImGuiHelpers.SettingLabel("Pitch", "Semitone shift applied by the clone layer.");
        if (ImGuiHelpers.LabeledSlider("##rvc_pitch", ref pitch, -12, 12, "Lower", "Higher", dt))
        {
            _rvc = _rvc with { F0UpKey = pitch };
            _configWriter.Write(_rvc);
        }
        ImGuiHelpers.SettingEndRow(rowY);

        // Preview with RVC applied
        rowY = ImGui.GetCursorPosY();
        ImGuiHelpers.SettingLabel("Preview", "Hear the cloned voice.");
        var clonePreviewId = "clone_preview";
        var cloneState =
            !enabled ? ImGuiHelpers.PreviewButtonState.Disabled
            : _audition.ActivePreviewId == clonePreviewId ? ImGuiHelpers.PreviewButtonState.Playing
            : _audition.IsPreviewing ? ImGuiHelpers.PreviewButtonState.Disabled
            : ImGuiHelpers.PreviewButtonState.Idle;
        if (ImGuiHelpers.PreviewButton("##clone_preview", cloneState, _elapsed))
        {
            if (cloneState == ImGuiHelpers.PreviewButtonState.Playing)
                _ = _audition.StopAsync();
            else
                _ = _audition.PreviewAsync(BuildPreviewRequest(mode, pitch, clonePreviewId));
        }
        ImGuiHelpers.SettingEndRow(rowY);

        if (!enabled)
            ImGui.EndDisabled();
    }

    private string[] GetRvcVoices()
    {
        // The provider returns a fresh IReadOnlyList<string> every call (disk
        // scan). We can't avoid that here, but we can avoid materialising a
        // fresh array every frame: reuse the cached copy as long as the
        // provider's list length matches. A mismatch (rare — voice directory
        // contents rarely change during a session) triggers a rebuild so
        // newly-added voices still appear in the picker.
        var live = _rvcVoiceProvider.GetAvailableVoices();
        if (live.Count == _rvcVoicesCache.Length)
        {
            return _rvcVoicesCache;
        }

        var buffer = new string[live.Count];
        for (var i = 0; i < live.Count; i++)
        {
            buffer[i] = live[i];
        }

        _rvcVoicesCache = buffer;
        return _rvcVoicesCache;
    }

    private VoiceAuditionRequest BuildPreviewRequest(VoiceMode mode, int pitch, string previewId)
    {
        var engineId = VoiceModeMapping.ToEngineId(mode);
        var tts = _ttsOptions.CurrentValue;
        var voice = mode == VoiceMode.Clear ? tts.Kokoro.DefaultVoice : tts.Qwen3.Speaker;
        float? expressiveness = mode == VoiceMode.Expressive ? tts.Qwen3.Temperature : null;

        return new VoiceAuditionRequest
        {
            Id = previewId,
            Engine = engineId,
            Voice = voice,
            Speed = tts.Kokoro.DefaultSpeed,
            Expressiveness = expressiveness,
            RvcEnabled = true,
            RvcVoice = _rvc.DefaultVoice,
            RvcPitchShift = pitch,
        };
    }
}
