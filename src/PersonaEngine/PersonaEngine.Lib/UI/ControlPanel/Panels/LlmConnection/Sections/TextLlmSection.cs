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
    private readonly IConfigWriter _configWriter;

    private readonly IUiThreadDispatcher _dispatcher;

    private readonly IDisposable? _onChangeSub;

    private readonly ILlmConnectionProbe _probe;

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

    private ProbeFooter.State _footerState;

    private readonly Action _onTestClicked;

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

        _onTestClicked = () => _ = _probe.ProbeAsync(LlmChannel.Text);
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
        SubsystemStatusChip.Render(subsystemStatus, _elapsed);

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted("The primary language model. Changes apply on the next turn.");
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private void RenderEndpointRow()
    {
        LlmChannelSection.EndpointRow(
            "Text",
            "Base URL of the OpenAI-compatible API.",
            ref _endpointBuf,
            out var next
        );
        if (next is not null)
        {
            WriteSnapshot(_snapshot with { TextEndpoint = _endpointBuf });
        }
    }

    private void RenderModelRow()
    {
        LlmChannelSection.ModelRow(
            "Which model to use for chat responses.",
            _probe.TextStatus.Status,
            _probe.TextStatus.AvailableModels,
            ref _modelBuf,
            _onTestClicked,
            out var next
        );
        if (next is not null)
        {
            WriteSnapshot(_snapshot with { TextModel = _modelBuf });
        }
    }

    private void RenderApiKeyRow()
    {
        LlmChannelSection.ApiKeyRow(
            "Text",
            ref _apiKeyBuf,
            ref _showKey,
            _endpointBuf,
            out var next
        );
        if (next is not null)
        {
            WriteSnapshot(_snapshot with { TextApiKey = _apiKeyBuf });
        }
    }

    private void RenderFooterRow(float dt)
    {
        ProbeFooter.Render(
            ref _footerState,
            _lastProbeTime,
            _probeInFlight,
            dt,
            _onTestClicked
        );
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
