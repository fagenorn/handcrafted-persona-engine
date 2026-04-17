using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.LLM.Connection;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;
using PersonaEngine.Lib.UI.Overlay;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.LlmConnection.Sections;

/// <summary>
///     Configure the optional vision LLM. A <see cref="ImGuiHelpers.ToggleSwitch" />
///     in the header controls <see cref="LlmOptions.VisionEnabled" />; when off, only
///     the header row renders. When on, exposes endpoint + model + API key with live
///     probe feedback via a broadcast-style <see cref="StatusPill" />, plus
///     <c>OpenAI · Custom</c> endpoint chips and an explicit "Test connection" button.
/// </summary>
public sealed class VisionLlmSection : IDisposable
{
    private const float ChipGap = 6f;

    private const string OpenAiUrl = "https://api.openai.com/v1";

    private readonly IConfigWriter _configWriter;

    private readonly ScannedNamePicker _modelPicker;

    private readonly IDisposable? _onChangeSub;

    private readonly ILlmConnectionProbe _probe;

    private AnimatedFloat _enableKnob;

    private string _apiKeyBuf = string.Empty;

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
        ILlmConnectionProbe probe
    )
    {
        _configWriter = configWriter;
        _probe = probe;
        _snapshot = options.CurrentValue;
        SyncBuffersFromSnapshot();

        _enableKnob = new AnimatedFloat(_snapshot.VisionEnabled ? 1f : 0f);

        // The picker's scan source is the probe's current vision model list —
        // refreshed whenever the probe fires StatusChanged for the vision channel.
        _modelPicker = new ScannedNamePicker(() => _probe.VisionStatus.AvailableModels);

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
        if (!_initialized)
        {
            _initialized = true;
            if (_snapshot.VisionEnabled)
            {
                _modelPicker.Refresh(_modelBuf);
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

        // Reserve header right-side space: pill + toggle when on, toggle only when off.
        var avail = ImGui.GetContentRegionAvail().X;
        var toggleW = 40f;
        var pillText = PillLabel(_probe.VisionStatus.Status);
        var pillWidth = ImGui.CalcTextSize(pillText).X + 32f;
        var rightW = visionOn ? pillWidth + 8f + toggleW : toggleW;

        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, avail - rightW));

        if (visionOn)
        {
            var pillStatus = MapToOverlay(_probe.VisionStatus.Status);
            StatusPill.Render(
                pillStatus,
                (float)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds,
                PillTransition.None,
                0f,
                _probe.VisionStatus.DetailMessage,
                StatusPillStyle.Broadcast,
                pillText
            );
            ImGui.SameLine(0f, 8f);
        }

        var on = visionOn;
        if (ImGuiHelpers.ToggleSwitch("##VisionEnabled", ref on, ref _enableKnob, dt))
        {
            WriteSnapshot(_snapshot with { VisionEnabled = on });
            if (on)
            {
                // Flipping ON — re-scan models and probe. No probe on flip OFF.
                _modelPicker.Refresh(_modelBuf);
                _ = _probe.ProbeAsync(LlmChannel.Vision);
            }
        }

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted("Lets the avatar see your screen. Used by Screen Awareness.");
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private void RenderEndpointRow()
    {
        var rowY = ImGui.GetCursorPosY();
        ImGuiHelpers.SettingLabel("Endpoint", "Vision-capable OpenAI-compatible URL.");
        if (ImGui.InputText("##VisionEndpoint", ref _endpointBuf, 512))
        {
            WriteSnapshot(_snapshot with { VisionEndpoint = _endpointBuf });
        }

        var ep = _endpointBuf;
        var isOpenAi = string.Equals(
            ep.TrimEnd('/'),
            OpenAiUrl.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase
        );
        var isCustom = !isOpenAi && !string.IsNullOrWhiteSpace(ep);

        if (ImGuiHelpers.Chip("OpenAI", isOpenAi))
        {
            _endpointBuf = OpenAiUrl;
            WriteSnapshot(_snapshot with { VisionEndpoint = _endpointBuf });
        }

        ImGui.SameLine(0f, ChipGap);
        // "Custom" chip is visual-only — clicking it doesn't mutate state because
        // the user is already typing a custom URL by definition.
        ImGuiHelpers.Chip("Custom", isCustom, interactive: false);

        ImGuiHelpers.SettingEndRow(rowY);
    }

    private void RenderModelRow()
    {
        var rowY = ImGui.GetCursorPosY();
        ImGuiHelpers.SettingLabel("Model", "Vision-capable model on the endpoint above.");

        var status = _probe.VisionStatus.Status;
        var haveModels =
            status is LlmProbeStatus.Reachable or LlmProbeStatus.ModelMissing
            && _probe.VisionStatus.AvailableModels.Count > 0;

        if (haveModels)
        {
            if (
                ImGuiHelpers.ScannedCombo(
                    "VisionModel",
                    _modelPicker,
                    _modelBuf,
                    out var picked,
                    onRefresh: () => _ = _probe.ProbeAsync(LlmChannel.Vision),
                    refreshTooltip: "Re-probe the endpoint to refresh the model list."
                )
            )
            {
                _modelBuf = picked;
                WriteSnapshot(_snapshot with { VisionModel = picked });
                _modelPicker.RecomputeMissing(_modelBuf);
            }

            if (_modelPicker.IsMissing && !string.IsNullOrEmpty(_modelBuf))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
                ImGui.TextUnformatted($"'{_modelBuf}' not served by this endpoint.");
                ImGui.PopStyleColor();
            }
        }
        else
        {
            if (ImGui.InputText("##VisionModel", ref _modelBuf, 256))
            {
                WriteSnapshot(_snapshot with { VisionModel = _modelBuf });
            }

            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
            ImGui.TextUnformatted("Connect first to pick from the endpoint's model list.");
            ImGui.PopStyleColor();
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

    private static string PillLabel(LlmProbeStatus status) =>
        status switch
        {
            LlmProbeStatus.Unknown => "Not tested yet",
            LlmProbeStatus.Probing => "Testing\u2026",
            LlmProbeStatus.Reachable => "Ready",
            LlmProbeStatus.ModelMissing => "Model not found",
            LlmProbeStatus.Unauthorized => "Auth failed",
            LlmProbeStatus.Unreachable => "Unreachable",
            LlmProbeStatus.InvalidUrl => "Invalid URL",
            LlmProbeStatus.Disabled => "Off",
            _ => "?",
        };

    private static OverlayStatus MapToOverlay(LlmProbeStatus status) =>
        status switch
        {
            LlmProbeStatus.Reachable => OverlayStatus.Active,
            LlmProbeStatus.Probing => OverlayStatus.Starting,
            LlmProbeStatus.ModelMissing => OverlayStatus.Starting,
            LlmProbeStatus.Unauthorized => OverlayStatus.Failed,
            LlmProbeStatus.Unreachable => OverlayStatus.Failed,
            LlmProbeStatus.InvalidUrl => OverlayStatus.Failed,
            LlmProbeStatus.Disabled => OverlayStatus.Off,
            _ => OverlayStatus.Off,
        };

    private void OnOptionsChanged(LlmOptions updated, string? _)
    {
        _snapshot = updated;
        if (!_initialized)
        {
            return;
        }

        SyncBuffersFromSnapshot();
        _modelPicker.RecomputeMissing(_modelBuf);
    }

    private void OnProbeStatus(LlmChannel ch)
    {
        if (ch != LlmChannel.Vision)
        {
            return;
        }

        var s = _probe.VisionStatus.Status;
        _probeInFlight = s == LlmProbeStatus.Probing;
        if (!_probeInFlight && s != LlmProbeStatus.Unknown)
        {
            _lastProbeTime = _probe.VisionStatus.ProbedAt;
        }

        // Probe just refreshed the available models list — re-run the scan so
        // the combo reflects the latest set and the missing flag is accurate.
        if (_initialized)
        {
            _modelPicker.Refresh(_modelBuf);
        }
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
