using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.LLM.Connection;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.LlmConnection;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;
using PersonaEngine.Lib.UI.ControlPanel.Threading;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.LlmConnection.Sections;

/// <summary>
///     Configure the primary text LLM. Endpoint + model + API key with live probe
///     feedback via a broadcast-style <see cref="SubsystemStatusChip" />, plus a preset
///     endpoint picker and an explicit "Test connection" button.
/// </summary>
public sealed class TextLlmSection : IDisposable
{
    private const string OpenAiUrl = "https://api.openai.com/v1";

    private const string LmStudioUrl = "http://localhost:1234/v1";

    private const string OllamaUrl = "http://localhost:11434/v1";

    private static readonly (string Label, string Url)[] EndpointPresets =
    [
        ("OpenAI", OpenAiUrl),
        ("LM Studio", LmStudioUrl),
        ("Ollama", OllamaUrl),
    ];

    private readonly IConfigWriter _configWriter;

    private readonly IUiThreadDispatcher _dispatcher;

    private readonly IDisposable? _onChangeSub;

    private readonly ILlmConnectionProbe _probe;

    private readonly ScannedNamePicker _modelPicker;

    private string _apiKeyBuf = string.Empty;

    private string _endpointBuf = string.Empty;

    private bool _initialized;

    private DateTimeOffset? _lastProbeTime;

    private string _modelBuf = string.Empty;

    private bool _probeInFlight;

    private bool _showKey;

    private LlmOptions _snapshot;

    private OneShotAnimation _testPulse;

    public TextLlmSection(
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

        // The picker's scan source is the probe's current model list — refreshed
        // whenever the probe fires StatusChanged for the text channel.
        _modelPicker = new ScannedNamePicker(() => _probe.TextStatus.AvailableModels);

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
            _modelPicker.Refresh(_modelBuf);
            _ = _probe.ProbeAsync(LlmChannel.Text);
        }

        using (Ui.Card("##text_llm", padding: 12f))
        {
            RenderHeader();
            RenderEndpointRow();
            RenderModelRow();
            RenderApiKeyRow();
            RenderFooterRow(dt);
        }
    }

    private void RenderHeader()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
        ImGui.TextUnformatted("Text LLM");
        ImGui.PopStyleColor();

        ImGui.SameLine();
        var subsystemStatus = LlmProbeStatusAdapter.ToSubsystemStatus(
            _probe.TextStatus.Status,
            _probe.TextStatus.DetailMessage
        );
        var avail = ImGui.GetContentRegionAvail().X;
        var pillWidth = ImGui.CalcTextSize(subsystemStatus.Label).X + 32f;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, avail - pillWidth));
        SubsystemStatusChip.Render(subsystemStatus);

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted("The primary language model. Changes apply on the next turn.");
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private void RenderEndpointRow()
    {
        var rowY = ImGui.GetCursorPosY();
        ImGuiHelpers.SettingLabel("Endpoint", "Base URL of the OpenAI-compatible API.");

        EndpointPickerRow.Render(
            "TextEndpoint",
            "Base URL of the OpenAI-compatible API.",
            EndpointPresets,
            _endpointBuf,
            out var next
        );

        if (next is not null)
        {
            _endpointBuf = next;
            WriteSnapshot(_snapshot with { TextEndpoint = _endpointBuf });
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }

    private void RenderModelRow()
    {
        var rowY = ImGui.GetCursorPosY();
        ImGuiHelpers.SettingLabel("Model", "Which model to use for chat responses.");

        var status = _probe.TextStatus.Status;
        var haveModels =
            status is LlmProbeStatus.Reachable or LlmProbeStatus.ModelMissing
            && _probe.TextStatus.AvailableModels.Count > 0;

        if (haveModels)
        {
            if (
                ImGuiHelpers.ScannedCombo(
                    "TextModel",
                    _modelPicker,
                    _modelBuf,
                    out var picked,
                    onRefresh: () => _ = _probe.ProbeAsync(LlmChannel.Text),
                    refreshTooltip: "Re-probe the endpoint to refresh the model list."
                )
            )
            {
                _modelBuf = picked;
                WriteSnapshot(_snapshot with { TextModel = picked });
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
            if (ImGui.InputText("##TextModel", ref _modelBuf, 256))
            {
                WriteSnapshot(_snapshot with { TextModel = _modelBuf });
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
        if (ImGui.InputText("##TextApiKey", ref _apiKeyBuf, 512, flags))
        {
            WriteSnapshot(_snapshot with { TextApiKey = _apiKeyBuf });
        }

        ImGui.SameLine();
        if (ImGuiHelpers.SubtleButton(_showKey ? "Hide##TextKey" : "Show##TextKey"))
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
            _ = _probe.ProbeAsync(LlmChannel.Text);
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
            _modelPicker.RecomputeMissing(_modelBuf);
        });
    }

    private void OnProbeStatus(LlmChannel ch)
    {
        if (ch != LlmChannel.Text)
        {
            return;
        }

        _dispatcher.Post(() =>
        {
            var s = _probe.TextStatus.Status;
            _probeInFlight = s == LlmProbeStatus.Probing;
            if (!_probeInFlight && s != LlmProbeStatus.Unknown)
            {
                _lastProbeTime = _probe.TextStatus.ProbedAt;
            }

            // Probe just refreshed the available models list — re-run the scan so
            // the combo reflects the latest set and the missing flag is accurate.
            if (_initialized)
            {
                _modelPicker.Refresh(_modelBuf);
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
        _endpointBuf = _snapshot.TextEndpoint ?? string.Empty;
        _modelBuf = _snapshot.TextModel ?? string.Empty;
        _apiKeyBuf = _snapshot.TextApiKey ?? string.Empty;
    }
}
