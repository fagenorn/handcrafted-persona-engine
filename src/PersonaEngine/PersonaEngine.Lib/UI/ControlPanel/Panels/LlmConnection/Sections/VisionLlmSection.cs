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

    private ProbeFooter.State _footerState;

    private readonly Action _onTestClicked;

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

        _onTestClicked = () => _ = _probe.ProbeAsync(LlmChannel.Vision);
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
        LlmChannelSection.EndpointRow(
            "Vision",
            "Vision-capable OpenAI-compatible URL.",
            ref _endpointBuf,
            out var next
        );
        if (next is not null)
        {
            WriteSnapshot(_snapshot with { VisionEndpoint = _endpointBuf });
        }
    }

    private void RenderModelRow()
    {
        LlmChannelSection.ModelRow(
            "Vision-capable model on the endpoint above.",
            _probe.VisionStatus.Status,
            _probe.VisionStatus.AvailableModels,
            ref _modelBuf,
            _onTestClicked,
            out var next
        );
        if (next is not null)
        {
            WriteSnapshot(_snapshot with { VisionModel = _modelBuf });
        }
    }

    private void RenderApiKeyRow()
    {
        LlmChannelSection.ApiKeyRow(
            "Vision",
            ref _apiKeyBuf,
            ref _showKey,
            _endpointBuf,
            out var next
        );
        if (next is not null)
        {
            WriteSnapshot(_snapshot with { VisionApiKey = _apiKeyBuf });
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
