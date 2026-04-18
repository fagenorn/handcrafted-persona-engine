using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Threading;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Sections;

/// <summary>
///     Dashboard row surfacing runtime controls: Cancel (active turn), Retry (error), Mute
///     (toggle). Orchestrator and mute-controller events are marshalled onto the UI thread
///     via <see cref="IUiThreadDispatcher" />.
///     <para>
///         Visually rendered as three custom-painted cards rather than ImGui.Button so the
///         row reads as a control surface: each card shows a bold action label, a one-line
///         helper that explains what clicking will do (or why it's disabled), and a
///         status-tinted accent. Disabled cards desaturate but keep their helper text so
///         the user knows the precondition. Matches the PresenceStripSection visual
///         language above (rounded plate, hover tint, clickable hit region via
///         <c>InvisibleButton</c>).
///     </para>
/// </summary>
public sealed class ControlsSection : IDisposable
{
    private const float CardHeight = 56f;
    private const float CardRounding = 8f;
    private const float CardGap = 12f;

    private readonly IUiThreadDispatcher _dispatcher;

    private readonly IMicMuteController _muteController;

    private readonly IConversationOrchestrator _orchestrator;

    private bool _disposed;

    private ConversationState _latestState = ConversationState.Initial;

    private bool _muted;

    public ControlsSection(
        IConversationOrchestrator orchestrator,
        IMicMuteController muteController,
        IUiThreadDispatcher dispatcher
    )
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(muteController);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _orchestrator = orchestrator;
        _muteController = muteController;
        _dispatcher = dispatcher;

        _muted = muteController.IsMuted;

        _orchestrator.StateChanged += OnStateChanged;
        _muteController.MutedChanged += OnMutedChanged;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _orchestrator.StateChanged -= OnStateChanged;
        _muteController.MutedChanged -= OnMutedChanged;
    }

    public void Render(float deltaTime)
    {
        _dispatcher.DrainPending();

        ImGuiHelpers.SectionHeader("Controls");

        var cancelEnabled = IsActiveTurn(_latestState);
        var retryEnabled = _latestState == ConversationState.Error;

        var cancel = new CardSpec(
            Id: "##ctl_cancel",
            Label: "Cancel",
            HelperEnabled: "Stop this turn",
            HelperDisabled: "No active turn",
            Accent: Theme.Error,
            Enabled: cancelEnabled,
            Emphasized: false,
            OnClick: () => _ = _orchestrator.CancelActiveTurnsAsync()
        );

        var retry = new CardSpec(
            Id: "##ctl_retry",
            Label: "Retry",
            HelperEnabled: "Recover from error",
            HelperDisabled: "Nothing to retry",
            Accent: Theme.Warning,
            Enabled: retryEnabled,
            Emphasized: false,
            OnClick: () => _ = _orchestrator.RetryErroredSessionsAsync()
        );

        // Mute is always clickable. When muted, the card lights up in the accent
        // color so the user has an obvious "the mic is off" indicator they can
        // click to undo.
        var mute = new CardSpec(
            Id: "##ctl_mute",
            Label: _muted ? "Unmute" : "Mute",
            HelperEnabled: _muted ? "Mic is off" : "Pause microphone",
            HelperDisabled: string.Empty,
            Accent: _muted ? Theme.AccentPrimary : Theme.TextSecondary,
            Enabled: true,
            Emphasized: _muted,
            OnClick: () => _muteController.SetMuted(!_muteController.IsMuted)
        );

        using var cols = Ui.EqualCols(3, CardHeight, gap: CardGap);

        if (cols.NextCol())
        {
            RenderCard(cancel);
        }

        if (cols.NextCol())
        {
            RenderCard(retry);
        }

        if (cols.NextCol())
        {
            RenderCard(mute);
        }
    }

    private static void RenderCard(CardSpec spec)
    {
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();

        // Guard against zero-size frames (same defensive check the health
        // cards apply) — InvisibleButton asserts on zero extent.
        if (avail.X <= 0f || avail.Y <= 0f)
        {
            return;
        }

        ImGui.InvisibleButton(spec.Id, avail);
        var hovered = ImGui.IsItemHovered() && spec.Enabled;
        var active = ImGui.IsItemActive() && spec.Enabled;
        var clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left) && spec.Enabled;

        if (hovered)
        {
            ImGuiHelpers.HandCursorOnHover();
        }

        var drawList = ImGui.GetWindowDrawList();
        DrawPlate(drawList, origin, avail, spec, hovered, active);
        DrawContent(drawList, origin, avail, spec, hovered);

        if (clicked)
        {
            spec.OnClick();
        }
    }

    private static void DrawPlate(
        ImDrawListPtr drawList,
        Vector2 origin,
        Vector2 size,
        CardSpec spec,
        bool hovered,
        bool active
    )
    {
        // Disabled cards use the flat neutral Surface2 palette (same as every
        // other "inert" panel), so they clearly read as NOT clickable. Enabled
        // cards carry a live accent tint — a visibly coloured fill + accented
        // border — so the affordance is obvious at a glance, not a 2% alpha
        // difference from disabled.
        Vector4 fill;
        if (!spec.Enabled)
        {
            fill = Theme.Surface2 with { W = 0.35f };
        }
        else if (active)
        {
            fill = spec.Accent with { W = 0.28f };
        }
        else if (hovered)
        {
            fill = spec.Accent with { W = 0.20f };
        }
        else if (spec.Emphasized)
        {
            fill = spec.Accent with { W = 0.16f };
        }
        else
        {
            fill = spec.Accent with { W = 0.10f };
        }

        ImGui.AddRectFilled(
            drawList,
            origin,
            origin + size,
            ImGui.ColorConvertFloat4ToU32(fill),
            CardRounding
        );

        // Border: dim + neutral when disabled, accent-tinted and brighter than
        // the default SurfaceBorder when enabled so the outline itself signals
        // interactivity (1 px — thicker borders clash with the rest of the
        // Dashboard's visual language).
        Vector4 borderColor;
        float borderThickness = 1f;
        if (!spec.Enabled)
        {
            borderColor = Theme.SurfaceBorder;
        }
        else if (active)
        {
            borderColor = spec.Accent with { W = 0.90f };
            borderThickness = 1.5f;
        }
        else if (hovered)
        {
            borderColor = spec.Accent with { W = 0.80f };
            borderThickness = 1.5f;
        }
        else if (spec.Emphasized)
        {
            borderColor = spec.Accent with { W = 0.65f };
        }
        else
        {
            borderColor = spec.Accent with { W = 0.45f };
        }

        ImGui.AddRect(
            drawList,
            origin,
            origin + size,
            ImGui.ColorConvertFloat4ToU32(borderColor),
            CardRounding,
            ImDrawFlags.None,
            borderThickness
        );
    }

    private static void DrawContent(
        ImDrawListPtr drawList,
        Vector2 origin,
        Vector2 size,
        CardSpec spec,
        bool hovered
    )
    {
        // Label: prominent, accent-tinted when enabled, dim when not.
        var labelColor = spec.Enabled
            ? (hovered || spec.Emphasized ? spec.Accent : Theme.TextPrimary)
            : Theme.TextTertiary;

        var helperText = spec.Enabled ? spec.HelperEnabled : spec.HelperDisabled;
        var helperColor = Theme.TextTertiary;

        var labelSize = ImGui.CalcTextSize(spec.Label);
        var helperSize = string.IsNullOrEmpty(helperText)
            ? Vector2.Zero
            : ImGui.CalcTextSize(helperText);

        // Stack label over helper, vertically centered in the card.
        var gap = string.IsNullOrEmpty(helperText) ? 0f : 4f;
        var blockHeight = labelSize.Y + gap + helperSize.Y;
        var labelY = origin.Y + (size.Y - blockHeight) * 0.5f;
        var labelX = origin.X + (size.X - labelSize.X) * 0.5f;

        drawList.AddText(
            new Vector2(labelX, labelY),
            ImGui.ColorConvertFloat4ToU32(labelColor),
            spec.Label
        );

        if (!string.IsNullOrEmpty(helperText))
        {
            var helperX = origin.X + (size.X - helperSize.X) * 0.5f;
            var helperY = labelY + labelSize.Y + gap;
            drawList.AddText(
                new Vector2(helperX, helperY),
                ImGui.ColorConvertFloat4ToU32(helperColor),
                helperText
            );
        }
    }

    private static bool IsActiveTurn(ConversationState state) =>
        state
            is ConversationState.ProcessingInput
                or ConversationState.WaitingForLlm
                or ConversationState.StreamingResponse
                or ConversationState.Speaking;

    private void OnStateChanged(ConversationState state) =>
        _dispatcher.Post(() => _latestState = state);

    private void OnMutedChanged(bool muted) => _dispatcher.Post(() => _muted = muted);

    private readonly record struct CardSpec(
        string Id,
        string Label,
        string HelperEnabled,
        string HelperDisabled,
        Vector4 Accent,
        bool Enabled,
        bool Emphasized,
        Action OnClick
    );
}
