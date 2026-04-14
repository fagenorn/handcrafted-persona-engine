using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Sections;

/// <summary>
///     All voice tuning knobs, collapsed by default. Contents vary by active mode.
///     Clear exposes Kokoro phoneme/speed knobs; Expressive exposes Qwen3 sampling knobs.
///     Both expose RVC processing quality and the raw engine id.
/// </summary>
public sealed class AdvancedSection : IDisposable
{
    private readonly IOptionsMonitor<TtsConfiguration> _ttsOptions;
    private readonly IOptionsMonitor<RVCFilterOptions> _rvcOptions;
    private readonly IConfigWriter _configWriter;

    private KokoroVoiceOptions _kokoro;
    private Qwen3TtsOptions _qwen3;
    private RVCFilterOptions _rvc;
    private readonly IDisposable? _ttsSubscription;
    private readonly IDisposable? _rvcSubscription;

    private AnimatedFloat _britishKnob;
    private AnimatedFloat _trimKnob;
    private AnimatedFloat _greedyKnob;
    private AnimatedFloat _silencePenaltyKnob;
    private bool _initialized;
    private readonly ImGuiHelpers.CollapsibleState _collapseState = new();

    public AdvancedSection(
        IOptionsMonitor<TtsConfiguration> ttsOptions,
        IOptionsMonitor<RVCFilterOptions> rvcOptions,
        IConfigWriter configWriter
    )
    {
        _ttsOptions = ttsOptions;
        _rvcOptions = rvcOptions;
        _configWriter = configWriter;

        var current = ttsOptions.CurrentValue;
        _kokoro = current.Kokoro;
        _qwen3 = current.Qwen3;
        _rvc = rvcOptions.CurrentValue;

        _ttsSubscription = ttsOptions.OnChange(
            (updated, _) =>
            {
                _kokoro = updated.Kokoro;
                _qwen3 = updated.Qwen3;
            }
        );
        _rvcSubscription = rvcOptions.OnChange((updated, _) => _rvc = updated);
    }

    public void Dispose()
    {
        _ttsSubscription?.Dispose();
        _rvcSubscription?.Dispose();
    }

    public void Render(float dt, VoiceMode mode)
    {
        if (!_initialized)
        {
            _britishKnob = new AnimatedFloat(_kokoro.UseBritishEnglish ? 1f : 0f);
            _trimKnob = new AnimatedFloat(_kokoro.TrimSilence ? 1f : 0f);
            _greedyKnob = new AnimatedFloat(_qwen3.CodePredictorGreedy ? 1f : 0f);
            _silencePenaltyKnob = new AnimatedFloat(_qwen3.SilencePenaltyEnabled ? 1f : 0f);
            _initialized = true;
        }

        ImGuiHelpers.CollapsibleSection(
            "Advanced",
            subtitle: null,
            defaultOpen: false,
            () => RenderBody(dt, mode),
            animState: _collapseState,
            dt: dt
        );
    }

    private void RenderBody(float dt, VoiceMode mode)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Warning);
        ImGui.TextUnformatted("These settings can degrade voice quality. Change only if you know what you're doing.");
        ImGui.PopStyleColor();
        ImGui.Spacing();

        if (mode == VoiceMode.Clear)
            RenderKokoroSettings(dt);
        else
            RenderQwen3Settings(dt);

        RenderRvcSettings();

        // Raw engine id — useful for scripting / bug reports
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
        ImGui.TextUnformatted($"Engine: {_ttsOptions.CurrentValue.ActiveEngine}");
        ImGui.PopStyleColor();
    }

    // ── Clear mode (Kokoro) ─────────────────────────────────────────────────

    private void RenderKokoroSettings(float dt)
    {
        float rowY;

        // Pace
        rowY = ImGui.GetCursorPosY();
        var speed = _kokoro.DefaultSpeed;
        ImGuiHelpers.SettingLabel("Pace", "Speaking speed multiplier (1.0 = normal).");
        if (ImGuiHelpers.LabeledSlider("##pace", ref speed, 0.5f, 2.0f, "Slow", "Fast", "%.2f", dt))
        {
            _kokoro = _kokoro with { DefaultSpeed = speed };
            _configWriter.Write(_kokoro);
        }
        ImGuiHelpers.SettingEndRow(rowY);

        // British English
        rowY = ImGui.GetCursorPosY();
        var british = _kokoro.UseBritishEnglish;
        ImGuiHelpers.SettingLabel("British English", "Use British English phoneme variants.");
        if (ImGuiHelpers.ToggleSwitch("##british", ref british, ref _britishKnob, dt))
        {
            _kokoro = _kokoro with { UseBritishEnglish = british };
            _configWriter.Write(_kokoro);
        }
        ImGuiHelpers.SettingEndRow(rowY);

        // Trim Silence
        rowY = ImGui.GetCursorPosY();
        var trim = _kokoro.TrimSilence;
        ImGuiHelpers.SettingLabel(
            "Trim Silence",
            "Strip leading/trailing silence from each audio segment."
        );
        if (ImGuiHelpers.ToggleSwitch("##trim", ref trim, ref _trimKnob, dt))
        {
            _kokoro = _kokoro with { TrimSilence = trim };
            _configWriter.Write(_kokoro);
        }
        ImGuiHelpers.SettingEndRow(rowY);

        // Max Segment Length
        rowY = ImGui.GetCursorPosY();
        var maxLen = _kokoro.MaxPhonemeLength;
        ImGuiHelpers.SettingLabel(
            "Max Segment Length",
            "Maximum phoneme length per synthesis segment (100\u2013510)."
        );
        if (ImGui.SliderInt("##maxlen", ref maxLen, 100, 510))
        {
            _kokoro = _kokoro with { MaxPhonemeLength = maxLen };
            _configWriter.Write(_kokoro);
        }
        ImGuiHelpers.SliderGlow(dt);
        ImGuiHelpers.SettingEndRow(rowY);
    }

    // ── Expressive mode (Qwen3) ─────────────────────────────────────────────

    private void RenderQwen3Settings(float dt)
    {
        float rowY;

        // Expressiveness (Temperature)
        rowY = ImGui.GetCursorPosY();
        var temperature = _qwen3.Temperature;
        ImGuiHelpers.SettingLabel("Expressiveness", "Sampling temperature \u2014 higher values produce more varied, expressive output.");
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

        // Top-K
        rowY = ImGui.GetCursorPosY();
        var topK = _qwen3.TopK;
        ImGuiHelpers.SettingLabel("Top-K", "Number of highest-probability tokens to sample from.");
        if (ImGui.SliderInt("##topk", ref topK, 1, 100))
        {
            _qwen3 = _qwen3 with { TopK = topK };
            _configWriter.Write(_qwen3);
        }
        ImGuiHelpers.SliderGlow(dt);
        ImGuiHelpers.SettingEndRow(rowY);

        // Top-P
        rowY = ImGui.GetCursorPosY();
        var topP = _qwen3.TopP;
        ImGuiHelpers.SettingLabel("Top-P", "Nucleus sampling threshold (0.0\u20131.0).");
        if (ImGui.SliderFloat("##topp", ref topP, 0.0f, 1.0f, "%.2f"))
        {
            _qwen3 = _qwen3 with { TopP = topP };
            _configWriter.Write(_qwen3);
        }
        ImGuiHelpers.SliderGlow(dt);
        ImGuiHelpers.SettingEndRow(rowY);

        // Repetition Penalty
        rowY = ImGui.GetCursorPosY();
        var rep = _qwen3.RepetitionPenalty;
        ImGuiHelpers.SettingLabel(
            "Repetition Penalty",
            "Penalises repeated tokens to reduce stuttering (1.0\u20131.5)."
        );
        if (ImGui.SliderFloat("##rep", ref rep, 1.0f, 1.5f, "%.2f"))
        {
            _qwen3 = _qwen3 with { RepetitionPenalty = rep };
            _configWriter.Write(_qwen3);
        }
        ImGuiHelpers.SliderGlow(dt);
        ImGuiHelpers.SettingEndRow(rowY);

        // Max Length
        rowY = ImGui.GetCursorPosY();
        var maxTok = _qwen3.MaxNewTokens;
        ImGuiHelpers.SettingLabel("Max Length", "Maximum tokens per utterance (256\u20134096).");
        if (ImGui.SliderInt("##maxtok", ref maxTok, 256, 4096))
        {
            _qwen3 = _qwen3 with { MaxNewTokens = maxTok };
            _configWriter.Write(_qwen3);
        }
        ImGuiHelpers.SliderGlow(dt);
        ImGuiHelpers.SettingEndRow(rowY);

        // Streaming Granularity
        rowY = ImGui.GetCursorPosY();
        var emit = _qwen3.EmitEveryFrames;
        ImGuiHelpers.SettingLabel(
            "Stream Granularity",
            "Emit audio every N frames \u2014 lower = faster first audio, higher = smoother."
        );
        if (ImGui.SliderInt("##emit", ref emit, 1, 32))
        {
            _qwen3 = _qwen3 with { EmitEveryFrames = emit };
            _configWriter.Write(_qwen3);
        }
        ImGuiHelpers.SliderGlow(dt);
        ImGuiHelpers.SettingEndRow(rowY);

        // Greedy Code Predictor
        rowY = ImGui.GetCursorPosY();
        var greedy = _qwen3.CodePredictorGreedy;
        ImGuiHelpers.SettingLabel(
            "Greedy Predictor",
            "Use argmax instead of sampling for spectral detail codes. May sound cleaner."
        );
        if (ImGuiHelpers.ToggleSwitch("##greedy", ref greedy, ref _greedyKnob, dt))
        {
            _qwen3 = _qwen3 with { CodePredictorGreedy = greedy };
            _configWriter.Write(_qwen3);
        }
        ImGuiHelpers.SettingEndRow(rowY);

        // Silence Penalty
        rowY = ImGui.GetCursorPosY();
        var silPen = _qwen3.SilencePenaltyEnabled;
        ImGuiHelpers.SettingLabel(
            "Silence Penalty",
            "Penalise long silent tails to prevent dead air at the end of utterances."
        );
        if (ImGuiHelpers.ToggleSwitch("##silpen", ref silPen, ref _silencePenaltyKnob, dt))
        {
            _qwen3 = _qwen3 with { SilencePenaltyEnabled = silPen };
            _configWriter.Write(_qwen3);
        }
        ImGuiHelpers.SettingEndRow(rowY);
    }

    // ── RVC (both modes) ────────────────────────────────────────────────────

    private void RenderRvcSettings()
    {
        var rowY = ImGui.GetCursorPosY();
        var hop = _rvc.HopSize;
        ImGuiHelpers.SettingLabel(
            "RVC Quality",
            "Hop size \u2014 smaller values are higher quality but slower (32\u2013256)."
        );
        if (ImGui.SliderInt("##hop", ref hop, 32, 256))
        {
            _rvc = _rvc with { HopSize = hop };
            _configWriter.Write(_rvc);
        }
        ImGuiHelpers.SettingEndRow(rowY);
    }
}
