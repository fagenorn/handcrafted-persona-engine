using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.Health;
using PersonaEngine.Lib.LLM.Connection;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.LlmConnection;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;
using PersonaEngine.Lib.UI.ControlPanel.Threading;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.LlmConnection.Sections;

/// <summary>
///     Configure the optional vision LLM. A <see cref="ImGuiHelpers.ToggleSwitch" />
///     in the header controls <see cref="LlmOptions.VisionEnabled" />; when off, only
///     the header row renders. When on, exposes endpoint + model + API key with live
///     probe feedback via a broadcast-style <see cref="SubsystemStatusChip" />, plus a
///     preset endpoint picker and an explicit "Test connection" button.
/// </summary>
public sealed class VisionLlmSection : IDisposable
{
    private readonly IConfigWriter _configWriter;

    private readonly IUiThreadDispatcher _dispatcher;

    private readonly IDisposable? _onChangeSub;

    private readonly ILlmConnectionProbe _probe;

    private AnimatedFloat _enableKnob;

    private string _apiKeyBuf = string.Empty;

    // Monotonic accumulator for the chip's live-pulse animation. Owning this
    // locally keeps the value small enough for float precision (see the
    // SubsystemStatusChip.Render doc comment).
    private float _elapsed;

    private string _endpointBuf = string.Empty;

    private bool _initialized;

    private DateTimeOffset? _lastProbeTime;

    private string _modelBuf = string.Empty;

    private bool _probeInFlight;

    private bool _showKey;

    private LlmOptions _snapshot;

    private OneShotAnimation _testPulse;

    public VisionLlmSection(
        IOptionsMonitor<LlmOptions> options,
        IConfigWriter configWriter,
        ILlmConnectionProbe probe,
        IUiThreadDispatcher dispatcher
    )
    {
        _configWriter = configWriter;
        _probe = probe;
        _dispatcher = dispatcher;
        _snapshot = options.CurrentValue;
        SyncBuffersFromSnapshot();

        _enableKnob = new AnimatedFloat(_snapshot.VisionEnabled ? 1f : 0f);

        _onChangeSub = options.OnChange(OnOptionsChanged);
        _probe.StatusChanged += OnProbeStatus;
    }

    public void Dispose()
    {
        _onChangeSub?.Dispose();
        _probe.StatusChanged -= OnProbeStatus;
    }

    public void Render(float dt)
    {
        _dispatcher.DrainPending();
        _elapsed += dt;

        if (!_initialized)
        {
            _initialized = true;
            if (_snapshot.VisionEnabled)
            {
                _ = _probe.ProbeAsync(LlmChannel.Vision);
            }
        }

        using (Ui.Card("##vision_llm", padding: 12f))
        {
            RenderHeader(dt);

            if (_snapshot.VisionEnabled)
            {
                RenderEndpointRow();
                RenderModelRow();
                RenderApiKeyRow();
                RenderFooterRow(dt);
            }
        }
    }

    private void RenderHeader(float dt)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
        ImGui.TextUnformatted("Vision LLM");
        ImGui.PopStyleColor();

        var visionOn = _snapshot.VisionEnabled;

        // Always render the chip — when the section is off, synthesise an "Off" status
        // so the toggle has a visual anchor and the user can see at a glance what state
        // the section is in. (The probe does not run when the channel is disabled, so
        // we cannot rely on _probe.VisionStatus to describe the off state.)
        var visionChipStatus = visionOn
            ? LlmProbeStatusAdapter.ToSubsystemStatus(
                _probe.VisionStatus.Status,
                _probe.VisionStatus.DetailMessage
            )
            : new SubsystemStatus(SubsystemHealth.Disabled, "Off", null);

        // Layout: "Vision LLM" ──────────── [toggle] [chip]. Chip matches the TextLlm
        // header exactly (right-aligned, no vertical offset, drawn at the label's
        // baseline); the toggle sits just to its left, pulled UP so its vertical
        // center aligns with the chip's midline.
        ImGui.SameLine();
        var avail = ImGui.GetContentRegionAvail().X;

        const float toggleW = 40f;
        // Gap between toggle and chip. The chip's broadcast tint extends 8 px LEFT of
        // its cursor, so a logical gap of 18 translates to ~10 px visible breathing
        // room — enough to not look like they're touching.
        const float toggleChipGap = 18f;
        // Chip visible width = tint padX (8) + dot (10) + dot-to-label gap (6) +
        // label + tint padX (8) = 32 + label.
        var chipW = ImGui.CalcTextSize(visionChipStatus.Label).X + 32f;
        var rightW = toggleW + toggleChipGap + chipW;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, avail - rightW));

        // Baseline Y of this header row — the chip is rendered at exactly this Y
        // (same as TextLlm's chip). The toggle is pulled up by FramePadding.Y to
        // cancel its built-in FrameHeight self-centering, so its 20 px track ends
        // up centered on the chip's textH-tall midline (both centers land at
        // baselineY + textH/2).
        var baselineY = ImGui.GetCursorPosY();
        ImGui.SetCursorPosY(baselineY - ImGui.GetStyle().FramePadding.Y);

        var on = visionOn;
        if (ImGuiHelpers.ToggleSwitch("##VisionEnabled", ref on, ref _enableKnob, dt))
        {
            WriteSnapshot(_snapshot with { VisionEnabled = on });
            if (on)
            {
                // Flipping ON — probe. No probe on flip OFF.
                _ = _probe.ProbeAsync(LlmChannel.Vision);
            }
        }

        ImGui.SameLine(0f, toggleChipGap);
        // SameLine restores cursor Y to the toggle's (offset) top; reset to the
        // original baseline so the chip renders in the same style as TextLlm.
        ImGui.SetCursorPosY(baselineY);
        SubsystemStatusChip.Render(visionChipStatus, _elapsed);

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted("Lets the avatar see your screen. Used by Screen Awareness.");
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private void RenderEndpointRow()
    {
        var rowY = ImGui.GetCursorPosY();
        ImGuiHelpers.SettingLabel("Endpoint", "Vision-capable OpenAI-compatible URL.");

        EndpointPickerRow.Render(
            "VisionEndpoint",
            EndpointPickerRow.DefaultPresets,
            _endpointBuf,
            out var next
        );

        if (next is not null)
        {
            _endpointBuf = next;
            WriteSnapshot(_snapshot with { VisionEndpoint = _endpointBuf });
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }

    private void RenderModelRow()
    {
        var rowY = ImGui.GetCursorPosY();
        ImGuiHelpers.SettingLabel("Model", "Vision-capable model on the endpoint above.");

        ScannedModelPicker.Render(
            _probe.VisionStatus.Status,
            _probe.VisionStatus.AvailableModels,
            _modelBuf,
            out var next,
            onRequestReprobe: () => _ = _probe.ProbeAsync(LlmChannel.Vision)
        );

        if (next is not null)
        {
            _modelBuf = next;
            WriteSnapshot(_snapshot with { VisionModel = _modelBuf });
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }

    private void RenderApiKeyRow()
    {
        var rowY = ImGui.GetCursorPosY();
        ImGuiHelpers.SettingLabel(
            "API Key",
            "Authentication token. Leave blank for local endpoints."
        );

        // Reserve horizontal space for the Hide/Show button so it doesn't overflow
        // the card. SubtleButton uses FramePadding(10, 5) so width ≈ text + 20.
        var visibleBtnText = _showKey ? "Hide" : "Show";
        var style = ImGui.GetStyle();
        var buttonW = ImGui.CalcTextSize(visibleBtnText).X + 20f;
        var avail = ImGui.GetContentRegionAvail().X;
        ImGui.SetNextItemWidth(MathF.Max(60f, avail - buttonW - style.ItemSpacing.X));

        var flags = _showKey ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password;
        if (ImGui.InputText("##VisionApiKey", ref _apiKeyBuf, 512, flags))
        {
            WriteSnapshot(_snapshot with { VisionApiKey = _apiKeyBuf });
        }

        ImGui.SameLine();
        if (ImGuiHelpers.SubtleButton(_showKey ? "Hide##VisionKey" : "Show##VisionKey"))
        {
            _showKey = !_showKey;
        }

        if (
            _endpointBuf.Contains("api.openai.com", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(_apiKeyBuf)
        )
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
            ImGui.TextUnformatted("OpenAI requires an API key.");
            ImGui.PopStyleColor();
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }

    private void RenderFooterRow(float dt)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
        ImGui.TextUnformatted(FooterLeftText());
        ImGui.PopStyleColor();

        ImGui.SameLine();
        var btnText = _probeInFlight ? "Testing\u2026" : "Test connection";
        var btnW = ImGui.CalcTextSize(btnText).X + 32f;
        var avail = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, avail - btnW));

        ImGui.BeginDisabled(_probeInFlight);
        if (ImGuiHelpers.PrimaryButtonWithFeedback(btnText, ref _testPulse, dt))
        {
            _ = _probe.ProbeAsync(LlmChannel.Vision);
        }

        ImGui.EndDisabled();
    }

    private string FooterLeftText()
    {
        if (_probeInFlight)
        {
            return "Testing\u2026";
        }

        if (_lastProbeTime is null)
        {
            return "Not tested yet";
        }

        return $"Last tested: {HumaniseAgo(DateTimeOffset.UtcNow - _lastProbeTime.Value)}";
    }

    private static string HumaniseAgo(TimeSpan ago)
    {
        if (ago.TotalSeconds < 1)
        {
            return "just now";
        }

        if (ago.TotalSeconds < 60)
        {
            return $"{(int)ago.TotalSeconds}s ago";
        }

        if (ago.TotalMinutes < 60)
        {
            return $"{(int)ago.TotalMinutes}m ago";
        }

        return $"{(int)ago.TotalHours}h ago";
    }

    private void OnOptionsChanged(LlmOptions updated, string? _)
    {
        _dispatcher.Post(() =>
        {
            _snapshot = updated;
            if (!_initialized)
            {
                return;
            }

            SyncBuffersFromSnapshot();
        });
    }

    private void OnProbeStatus(LlmChannel ch)
    {
        if (ch != LlmChannel.Vision)
        {
            return;
        }

        _dispatcher.Post(() =>
        {
            var s = _probe.VisionStatus.Status;
            _probeInFlight = s == LlmProbeStatus.Probing;
            if (!_probeInFlight && s != LlmProbeStatus.Unknown)
            {
                _lastProbeTime = _probe.VisionStatus.ProbedAt;
            }
        });
    }

    private void WriteSnapshot(LlmOptions next)
    {
        _snapshot = next;
        _configWriter.Write(next);
    }

    private void SyncBuffersFromSnapshot()
    {
        _endpointBuf = _snapshot.VisionEndpoint ?? string.Empty;
        _modelBuf = _snapshot.VisionModel ?? string.Empty;
        _apiKeyBuf = _snapshot.VisionApiKey ?? string.Empty;
    }
}
