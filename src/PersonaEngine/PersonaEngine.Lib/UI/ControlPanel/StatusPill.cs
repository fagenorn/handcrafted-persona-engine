using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.Overlay;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
///     One-shot animation a caller can request on a status transition. The
///     caller decides which kind to emit based on the previous / current
///     status pair; <see cref="StatusPill" /> just renders it.
/// </summary>
public enum PillTransition
{
    /// <summary>No one-shot animation in flight.</summary>
    None,

    /// <summary>Outward ring "boom" — used when going from a dark state to Active.</summary>
    Ignite,

    /// <summary>Inward ring collapse — used when leaving Active toward Off / Stopping.</summary>
    Extinguish,

    /// <summary>Dot colour flash — used when entering Failed.</summary>
    Alert,
}

/// <summary>
///     Optional style knobs for <see cref="StatusPill.Render" />. Defaults
///     produce the minimal "dot + label" that the Overlay panel has always
///     shown; callers that want the broadcast-style badge (Dashboard)
///     opt-in via the flags.
/// </summary>
public readonly record struct StatusPillStyle(
    bool ShowBackgroundTint = false,
    bool EnableLivePulse = false,
    bool EnableTransitions = false
)
{
    /// <summary>Dashboard preset: tint, steady live pulse on Active, and one-shot transitions.</summary>
    public static StatusPillStyle Broadcast { get; } =
        new(ShowBackgroundTint: true, EnableLivePulse: true, EnableTransitions: true);
}

/// <summary>
///     Renders the "dot + label" status indicator used by the Overlay panel and
///     the Dashboard presence badge. Two motion languages:
///     <list type="bullet">
///         <item>
///             <description>
///                 <b>Dot alpha pulse</b> at <see cref="TransitionalPulseHz" /> —
///                 "work in flight" (Starting / Stopping).
///             </description>
///         </item>
///         <item>
///             <description>
///                 <b>Expanding ring halo</b> at <see cref="LivePulseSeconds" />s —
///                 "this is live right now" (Active, when
///                 <see cref="StatusPillStyle.EnableLivePulse" /> is set).
///             </description>
///         </item>
///     </list>
///     On top of those, callers can request a one-shot <see cref="PillTransition" />
///     that fires for a fixed duration when the overlay state changes.
/// </summary>
public static class StatusPill
{
    // Pulse frequency for transitional states. 1.2 Hz reads as a gentle heartbeat
    // without being distracting.
    private const float TransitionalPulseHz = 1.2f;

    // Live-broadcast ring cycle — matches the conventional ~1.6s used by
    // Twitch / YouTube style LIVE indicators (CSS3Shapes reference).
    private const float LivePulseSeconds = 1.6f;

    // One-shot durations. Ignite is the loudest since it announces "just went live";
    // extinguish fades out quickly, alert flashes briefly.
    private const float IgniteDurationSeconds = 0.4f;
    private const float ExtinguishDurationSeconds = 0.2f;
    private const float AlertDurationSeconds = 0.3f;

    private const float DotRadius = 5f;

    /// <summary>
    ///     Renders a basic status pill (dot + label) inline at the current cursor
    ///     position. No live pulse, no transitions — matches the original Overlay
    ///     panel look.
    /// </summary>
    /// <param name="overrideLabel">
    ///     Optional domain-specific label; see the full overload for details.
    ///     <c>null</c> keeps the default label from <see cref="OverlayStatus"/>.
    /// </param>
    public static void Render(
        OverlayStatus status,
        float elapsed,
        string? lastError,
        string? overrideLabel = null
    ) =>
        Render(
            status,
            elapsed,
            PillTransition.None,
            secondsSinceTransition: 0f,
            lastError,
            style: default,
            overrideLabel: overrideLabel
        );

    /// <summary>
    ///     Renders a status pill with optional broadcast-style visuals. The pill
    ///     is wrapped in <c>BeginGroup</c>/<c>EndGroup</c> so callers can place
    ///     an <c>InvisibleButton</c> over <c>GetItemRectMin/Max</c> to make it
    ///     clickable.
    /// </summary>
    /// <param name="status">Current overlay lifecycle status.</param>
    /// <param name="elapsed">
    ///     Accumulated seconds (monotonic) for continuous animations — pass sum of deltaTimes.
    /// </param>
    /// <param name="transition">
    ///     Which one-shot effect to render. <see cref="PillTransition.None" /> disables one-shots.
    /// </param>
    /// <param name="secondsSinceTransition">
    ///     Seconds since <paramref name="transition" /> started. Ignored when transition is None.
    /// </param>
    /// <param name="lastError">Shown as a tooltip when status is Failed.</param>
    /// <param name="style">Broadcast knobs; <c>default</c> is the minimal pill.</param>
    /// <param name="overrideLabel">
    ///     When non-null, replaces the default status label with domain-specific text
    ///     (e.g. "Ready", "Model not found", "Auth failed"). The colour, pulse, and
    ///     ring are still driven by <paramref name="status" />.
    /// </param>
    public static void Render(
        OverlayStatus status,
        float elapsed,
        PillTransition transition,
        float secondsSinceTransition,
        string? lastError,
        StatusPillStyle style,
        string? overrideLabel = null
    )
    {
        var (color, _, transitionalPulse) = StatusVisuals(status);
        var label = ResolveLabel(status, overrideLabel);

        ImGui.BeginGroup();

        var drawList = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();
        var textH = ImGui.GetTextLineHeight();
        var dotCenter = new Vector2(cursor.X + DotRadius, cursor.Y + textH * 0.5f);

        // 1) Background tint — drawn first so everything else renders on top.
        if (style.ShowBackgroundTint)
        {
            DrawBackgroundTint(drawList, cursor, color, textH, label);
        }

        // 2) Live ring halo (steady-state) for Active.
        if (style.EnableLivePulse && status == OverlayStatus.Active)
        {
            DrawLiveRing(drawList, dotCenter, color, elapsed);
        }

        // 3) One-shot transition ring / flash. Draws after the live ring so an
        //    Ignite overlaps cleanly with the first live-pulse cycle.
        var dotColor = color;
        if (style.EnableTransitions && transition != PillTransition.None)
        {
            dotColor = ApplyOneShot(drawList, dotCenter, color, transition, secondsSinceTransition);
        }

        // 4) Transitional dot pulse (Starting / Stopping) — modulate dot alpha.
        if (transitionalPulse)
        {
            var t = 0.75f + 0.25f * MathF.Sin(elapsed * 2f * MathF.PI * TransitionalPulseHz);
            dotColor = dotColor with { W = t };
        }

        // 5) Dot.
        ImGuiHelpers.StatusDot(dotColor, DotRadius);
        ImGui.SameLine(0f, 6f);

        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();

        ImGui.EndGroup();

        if (status == OverlayStatus.Failed && !string.IsNullOrEmpty(lastError))
        {
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(lastError);
            }
        }
    }

    /// <summary>
    ///     Maps the natural transition between two statuses to the one-shot
    ///     animation to play. Callers supply <c>prev</c> and <c>curr</c>; this
    ///     keeps the choreography in one place instead of scattered across
    ///     panels.
    /// </summary>
    public static PillTransition TransitionFor(OverlayStatus prev, OverlayStatus curr)
    {
        if (prev == curr)
        {
            return PillTransition.None;
        }

        return curr switch
        {
            OverlayStatus.Active => PillTransition.Ignite,
            OverlayStatus.Failed => PillTransition.Alert,
            OverlayStatus.Off or OverlayStatus.Stopping when prev == OverlayStatus.Active =>
                PillTransition.Extinguish,
            _ => PillTransition.None,
        };
    }

    /// <summary>
    ///     Returns the duration of a one-shot transition in seconds — callers
    ///     use this to know when to clear their transition state and stop
    ///     feeding <c>secondsSinceTransition</c>.
    /// </summary>
    public static float DurationOf(PillTransition transition) =>
        transition switch
        {
            PillTransition.Ignite => IgniteDurationSeconds,
            PillTransition.Extinguish => ExtinguishDurationSeconds,
            PillTransition.Alert => AlertDurationSeconds,
            _ => 0f,
        };

    /// <summary>
    ///     Resolves the label to display on the pill. A non-null <paramref name="overrideLabel"/>
    ///     is returned as-is; otherwise the default label for the <paramref name="status"/> is used.
    ///     An empty string is a legitimate override (it produces a "dot only" pill) and is
    ///     returned unchanged — callers should not short-circuit via <c>string.IsNullOrEmpty</c>.
    /// </summary>
    internal static string ResolveLabel(OverlayStatus status, string? overrideLabel)
    {
        if (overrideLabel is not null)
        {
            return overrideLabel;
        }

        return StatusVisuals(status).Label;
    }

    private static (Vector4 Color, string Label, bool TransitionalPulse) StatusVisuals(
        OverlayStatus status
    ) =>
        status switch
        {
            OverlayStatus.Off => (Theme.TextTertiary, "Off", false),
            OverlayStatus.Starting => (Theme.Warning, "Starting…", true),
            OverlayStatus.Active => (Theme.Success, "Active", false),
            OverlayStatus.Stopping => (Theme.Warning, "Stopping…", true),
            OverlayStatus.Failed => (Theme.Error, "Failed — click to retry", false),
            _ => (Theme.TextTertiary, "?", false),
        };

    private static void DrawBackgroundTint(
        ImDrawListPtr drawList,
        Vector2 cursor,
        Vector4 color,
        float textH,
        string label
    )
    {
        const float padX = 8f;
        const float padY = 4f;
        const float rounding = 6f;

        var labelW = ImGui.CalcTextSize(label).X;
        var width = padX + DotRadius * 2f + 6f + labelW + padX;
        var height = MathF.Max(textH, DotRadius * 2f) + padY * 2f;

        var min = new Vector2(cursor.X - padX, cursor.Y - padY);
        var max = new Vector2(min.X + width, min.Y + height);
        var tint = color with { W = 0.10f };
        ImGui.AddRectFilled(drawList, min, max, ImGui.ColorConvertFloat4ToU32(tint), rounding);
    }

    private static void DrawLiveRing(
        ImDrawListPtr drawList,
        Vector2 center,
        Vector4 color,
        float elapsed
    )
    {
        // Normalised phase [0, 1) through the 1.6s cycle.
        var phase = (elapsed % LivePulseSeconds) / LivePulseSeconds;

        // Radius expands 1× → 3× of the dot radius; alpha fades 0.75 → 0.
        var radius = DotRadius * (1f + phase * 2f);
        var alpha = 0.75f * (1f - phase);
        if (alpha <= 0.01f)
        {
            return;
        }

        var ringColor = color with { W = alpha };
        ImGui.AddCircle(
            drawList,
            center,
            radius,
            ImGui.ColorConvertFloat4ToU32(ringColor),
            numSegments: 0,
            thickness: 1.5f
        );
    }

    private static Vector4 ApplyOneShot(
        ImDrawListPtr drawList,
        Vector2 center,
        Vector4 color,
        PillTransition transition,
        float secondsSinceTransition
    )
    {
        var duration = DurationOf(transition);
        if (duration <= 0f || secondsSinceTransition < 0f || secondsSinceTransition >= duration)
        {
            return color;
        }

        var t = secondsSinceTransition / duration;

        switch (transition)
        {
            case PillTransition.Ignite:
            {
                // Single expanding ring: 1× → 4×, opacity 1 → 0.
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
                // Collapsing ring: 3× → 0.5×, opacity 0.75 → 0.
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
                // Dot flashes white → error red. Lerp tint on the dot itself.
                var white = new Vector4(1f, 1f, 1f, 1f);
                return Vector4.Lerp(white, color, t);
            }

            default:
                return color;
        }
    }
}
