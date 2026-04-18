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
    private const string OpenAiUrl = "https://api.openai.com/v1";

    private static readonly (string Label, string Url)[] EndpointPresets = [("OpenAI", OpenAiUrl)];

    private readonly IConfigWriter _configWriter;

    private readonly IUiThreadDispatcher _dispatcher;

    private readonly IDisposable? _onChangeSub;

    private readonly ILlmConnectionProbe _probe;

    private AnimatedFloat _enableKnob;

    private string _apiKeyBuf = string.Empty;

    private string _endpointBuf = string.Empty;

    private bool _initialized;

    private DateTimeOffset? _lastProbeTime;

    private string _modelBuf = string.Empty;

    private bool _advancedModelOpen;

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

        var avail = ImGui.GetContentRegionAvail().X;
        var toggleW = 40f;
        var pillWidth = ImGui.CalcTextSize(visionChipStatus.Label).X + 32f;
        var rightW = pillWidth + 8f + toggleW;

        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, avail - rightW));

        SubsystemStatusChip.Render(visionChipStatus);
        ImGui.SameLine(0f, 8f);

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
            "Vision-capable OpenAI-compatible URL.",
            EndpointPresets,
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
            onRequestReprobe: () => _ = _probe.ProbeAsync(LlmChannel.Vision),
            ref _advancedModelOpen
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
