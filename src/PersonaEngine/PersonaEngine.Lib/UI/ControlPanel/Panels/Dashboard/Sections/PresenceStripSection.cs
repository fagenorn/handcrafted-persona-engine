using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.Overlay;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Sections;

/// <summary>
///     Full-width "presence strip" pinned at the top of the Dashboard. One
///     prominent, labelled, click-anywhere control for the desktop overlay:
///     <c>[pulse]  Overlay &nbsp;·&nbsp; &lt;status word&gt; &nbsp;&nbsp;&nbsp; &lt;action verb&gt;</c>.
///     <para>
///         Design intent (user feedback driven): the Overlay toggle previously
///         lived as a tiny badge tucked into the System Health header and read
///         as "an indicator, not a control." This strip fixes that by being
///         obviously clickable (hover glow, hand cursor, whole-row hit target),
///         explicitly labelled ("Overlay"), and paired with an action verb that
///         tells the user what clicking <em>does</em> in the current state —
///         "Hide" when Active, "Show" when Off, "Retry" when Failed.
///     </para>
///     <para>
///         Animation reuses the <see cref="StatusPill" /> transition vocabulary
///         (Ignite / Extinguish / Alert + live pulse) but renders its own
///         custom strip layout — it is not a wrapped pill.
///     </para>
/// </summary>
public sealed class PresenceStripSection(OverlayHost overlayHost)
{
    /// <summary>
    ///     Target pixel height of the strip. Slim on purpose — the strip sits
    ///     above the health row and shouldn't compete with it for weight. Still
    ///     clears the WCAG 2.5.5 44px hit-target guideline when including the
    ///     row's own item-spacing.
    /// </summary>
    public const float StripHeight = 34f;

    // Live-broadcast ring cycle period — matches StatusPill's LivePulseSeconds
    // so Active state animation reads consistently across the app.
    private const float LivePulseSeconds = 1.6f;

    // Transitional dot-alpha pulse (Starting / Stopping) — gentle heartbeat.
    private const float TransitionalPulseHz = 1.2f;

    // Dot geometry inside the strip.
    private const float DotRadius = 5f;

    // Strip visual knobs. Rounding scales with height so the plate reads as a
    // soft pill rather than a chunky card.
    private const float Rounding = 8f;
    private const float PaddingX = 12f;

    // Monotonic time accumulator for continuous animations.
    private float _elapsed;

    // One-shot transition tracking — mirrors the pattern OverlayPanel /
    // SystemHealthSection used for StatusPill.
    private OverlayStatus _prevStatus = OverlayStatus.Off;
    private PillTransition _transition = PillTransition.None;
    private float _secondsSinceTransition;

    public void Render(float dt)
    {
        _elapsed += dt;
        UpdateTransitionState(overlayHost.Status, dt);

        var drawList = ImGui.GetWindowDrawList();
        var availW = ImGui.GetContentRegionAvail().X;
        var stripSize = new Vector2(availW, StripHeight);

        // 1) Claim the hit region first so the whole strip is clickable.
        var origin = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##overlay_presence_strip", stripSize);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        if (hovered)
        {
            ImGuiHelpers.HandCursorOnHover();
            if (
                overlayHost.Status == OverlayStatus.Failed
                && !string.IsNullOrEmpty(overlayHost.LastError)
            )
            {
                ImGui.SetTooltip(overlayHost.LastError);
            }
        }

        var status = overlayHost.Status;
        var (statusColor, statusWord) = StatusVisuals(status);

        // 2) Background plate — status-tinted, brighter on hover / press.
        DrawBackground(drawList, origin, stripSize, statusColor, hovered, active);

        // 3) Left: ring pulse + solid dot, vertically centered.
        var dotCenter = new Vector2(origin.X + PaddingX + DotRadius, origin.Y + StripHeight * 0.5f);
        DrawDotAndHalo(drawList, dotCenter, status, statusColor);

        // 4) Middle: "Overlay" label (bold-weight via accent color) · status word.
        var textY = origin.Y + (StripHeight - ImGui.GetTextLineHeight()) * 0.5f;
        var labelX = dotCenter.X + DotRadius + 14f;
        DrawLabelAndStatus(drawList, labelX, textY, statusColor, statusWord);

        // 5) Right: action verb, right-aligned, showing what the click will do.
        DrawActionVerb(
            drawList,
            origin.X + stripSize.X - PaddingX,
            textY,
            status,
            overlayHost.DesiredEnabled,
            hovered
        );

        if (clicked)
        {
            overlayHost.SetEnabled(!overlayHost.DesiredEnabled);
        }
    }

    private void UpdateTransitionState(OverlayStatus current, float dt)
    {
        if (current != _prevStatus)
        {
            _transition = StatusPill.TransitionFor(_prevStatus, current);
            _secondsSinceTransition = 0f;
            _prevStatus = current;
            return;
        }

        if (_transition == PillTransition.None)
        {
            return;
        }

        _secondsSinceTransition += dt;
        if (_secondsSinceTransition >= StatusPill.DurationOf(_transition))
        {
            _transition = PillTransition.None;
            _secondsSinceTransition = 0f;
        }
    }

    private static (Vector4 Color, string Word) StatusVisuals(OverlayStatus status) =>
        status switch
        {
            OverlayStatus.Off => (Theme.TextTertiary, "Off"),
            OverlayStatus.Starting => (Theme.Warning, "Starting…"),
            OverlayStatus.Active => (Theme.Success, "Live"),
            OverlayStatus.Stopping => (Theme.Warning, "Stopping…"),
            OverlayStatus.Failed => (Theme.Error, "Failed"),
            _ => (Theme.TextTertiary, "?"),
        };

    private static string ActionVerb(OverlayStatus status, bool desiredEnabled) =>
        status switch
        {
            OverlayStatus.Active => "Hide",
            OverlayStatus.Failed => "Retry",
            OverlayStatus.Starting or OverlayStatus.Stopping => desiredEnabled ? "Cancel" : "…",
            _ => "Show",
        };

    private static void DrawBackground(
        ImDrawListPtr drawList,
        Vector2 origin,
        Vector2 size,
        Vector4 statusColor,
        bool hovered,
        bool active
    )
    {
        // Always-on plate: the strip is a persistent surface, but kept very
        // quiet at rest so it doesn't compete with the health cards. Hover /
        // press lift the tint toward the status color to signal clickability.
        float baseAlpha;
        if (active)
            baseAlpha = 0.16f;
        else if (hovered)
            baseAlpha = 0.10f;
        else
            baseAlpha = 0.05f;

        var fill = statusColor with { W = baseAlpha };
        ImGui.AddRectFilled(
            drawList,
            origin,
            origin + size,
            ImGui.ColorConvertFloat4ToU32(fill),
            Rounding
        );

        var borderColor = hovered ? statusColor with { W = 0.40f } : Theme.SurfaceBorder;
        ImGui.AddRect(
            drawList,
            origin,
            origin + size,
            ImGui.ColorConvertFloat4ToU32(borderColor),
            Rounding,
            ImDrawFlags.None,
            1f
        );
    }

    private void DrawDotAndHalo(
        ImDrawListPtr drawList,
        Vector2 center,
        OverlayStatus status,
        Vector4 statusColor
    )
    {
        // Steady-state live ring halo — only when Active.
        if (status == OverlayStatus.Active)
        {
            var phase = (_elapsed % LivePulseSeconds) / LivePulseSeconds;
            var radius = DotRadius * (1f + phase * 2f);
            var alpha = 0.75f * (1f - phase);
            if (alpha > 0.01f)
            {
                var ring = statusColor with { W = alpha };
                ImGui.AddCircle(
                    drawList,
                    center,
                    radius,
                    ImGui.ColorConvertFloat4ToU32(ring),
                    numSegments: 0,
                    thickness: 1.5f
                );
            }
        }

        // One-shot transition effect on top of the live ring.
        var dotColor = statusColor;
        if (_transition != PillTransition.None)
        {
            dotColor = ApplyOneShot(drawList, center, statusColor);
        }

        // Transitional heartbeat alpha pulse on the dot itself (Starting / Stopping).
        if (status is OverlayStatus.Starting or OverlayStatus.Stopping)
        {
            var t = 0.75f + 0.25f * MathF.Sin(_elapsed * 2f * MathF.PI * TransitionalPulseHz);
            dotColor = dotColor with { W = t };
        }

        ImGui.AddCircleFilled(drawList, center, DotRadius, ImGui.ColorConvertFloat4ToU32(dotColor));
    }

    private Vector4 ApplyOneShot(ImDrawListPtr drawList, Vector2 center, Vector4 color)
    {
        var duration = StatusPill.DurationOf(_transition);
        if (duration <= 0f)
            return color;

        var t = Math.Clamp(_secondsSinceTransition / duration, 0f, 1f);

        switch (_transition)
        {
            case PillTransition.Ignite:
            {
                var radius = DotRadius * (1f + t * 3f);
                var alpha = 1f - t;
                var ring = color with { W = alpha };
                ImGui.AddCircle(
                    drawList,
                    center,
                    radius,
                    ImGui.ColorConvertFloat4ToU32(ring),
                    numSegments: 0,
                    thickness: 2f
                );
                return color;
            }
            case PillTransition.Extinguish:
            {
                var radius = DotRadius * (3f - t * 2.5f);
                var alpha = 0.75f * (1f - t);
                var ring = color with { W = alpha };
                ImGui.AddCircle(
                    drawList,
                    center,
                    radius,
                    ImGui.ColorConvertFloat4ToU32(ring),
                    numSegments: 0,
                    thickness: 1.5f
                );
                return color;
            }
            case PillTransition.Alert:
            {
                var white = new Vector4(1f, 1f, 1f, 1f);
                return Vector4.Lerp(white, color, t);
            }
            default:
                return color;
        }
    }

    private static void DrawLabelAndStatus(
        ImDrawListPtr drawList,
        float x,
        float y,
        Vector4 statusColor,
        string statusWord
    )
    {
        const string label = "Overlay";
        const string separator = "  ·  ";

        var labelCol = ImGui.ColorConvertFloat4ToU32(Theme.TextPrimary);
        var sepCol = ImGui.ColorConvertFloat4ToU32(Theme.TextTertiary);
        var statusCol = ImGui.ColorConvertFloat4ToU32(statusColor);

        var cursorX = x;
        drawList.AddText(new Vector2(cursorX, y), labelCol, label);
        cursorX += ImGui.CalcTextSize(label).X;

        drawList.AddText(new Vector2(cursorX, y), sepCol, separator);
        cursorX += ImGui.CalcTextSize(separator).X;

        drawList.AddText(new Vector2(cursorX, y), statusCol, statusWord);
    }

    private static void DrawActionVerb(
        ImDrawListPtr drawList,
        float rightEdgeX,
        float y,
        OverlayStatus status,
        bool desiredEnabled,
        bool hovered
    )
    {
        var verb = ActionVerb(status, desiredEnabled);
        var size = ImGui.CalcTextSize(verb);
        var textX = rightEdgeX - size.X;

        var color = hovered ? Theme.TextPrimary : Theme.TextSecondary;
        drawList.AddText(new Vector2(textX, y), ImGui.ColorConvertFloat4ToU32(color), verb);
    }
}
