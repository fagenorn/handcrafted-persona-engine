using Hexa.NET.ImGui;
using PersonaEngine.Lib.Health;
using PersonaEngine.Lib.UI.Overlay;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;

/// <summary>
///     Shared status chip for Dashboard health cards and LLM section headers.
///     Maps <see cref="SubsystemHealth" /> to <see cref="OverlayStatus" /> and
///     renders through <see cref="StatusPill" /> with a broadcast-style badge.
/// </summary>
public static class SubsystemStatusChip
{
    /// <summary>
    ///     Maps a <see cref="SubsystemHealth" /> bucket to the corresponding
    ///     <see cref="OverlayStatus" /> used by <see cref="StatusPill" />.
    /// </summary>
    public static OverlayStatus MapHealth(SubsystemHealth health) =>
        health switch
        {
            SubsystemHealth.Healthy => OverlayStatus.Active,
            SubsystemHealth.Degraded => OverlayStatus.Degraded,
            SubsystemHealth.Failed => OverlayStatus.Failed,
            SubsystemHealth.Disabled => OverlayStatus.Off,
            SubsystemHealth.Muted => OverlayStatus.Muted,
            SubsystemHealth.Unknown => OverlayStatus.Unknown,
            _ => OverlayStatus.Unknown,
        };

    /// <summary>
    ///     Renders a broadcast-style status pill for the given <paramref name="status" />.
    ///     Uses <see cref="StatusPill.Render" /> with <see cref="StatusPillStyle.Broadcast" />
    ///     and surfaces <see cref="SubsystemStatus.Detail" /> as a tooltip for non-Failed states
    ///     (Failed tooltips are already handled by <see cref="StatusPill" /> via <c>lastError</c>).
    /// </summary>
    /// <param name="status">The subsystem observation to render.</param>
    /// <param name="elapsed">
    ///     Monotonic seconds accumulated by the caller (sum of per-frame dt).
    ///     Feeds <see cref="StatusPill" />'s live-ring phase. Do NOT pass a
    ///     UTC-epoch value here — <c>float</c> loses enough precision at that
    ///     magnitude that <c>elapsed % 1.6f</c> snaps to a constant and the
    ///     ring stops animating. Each caller should own a <c>_elapsed += dt</c>
    ///     field.
    /// </param>
    /// <param name="style">
    ///     Optional style override; defaults to <see cref="StatusPillStyle.Broadcast" />.
    /// </param>
    /// <param name="maxLabelWidth">
    ///     Optional pixel budget for the label text (does NOT include the chip chrome —
    ///     padding, dot, gap — which add ~32 px around it). When set and the full label
    ///     exceeds the budget, the label is truncated with an ellipsis and the full
    ///     original text is surfaced as a tooltip. Zero (default) disables truncation.
    /// </param>
    public static void Render(
        SubsystemStatus status,
        float elapsed,
        StatusPillStyle? style = null,
        float maxLabelWidth = 0f
    )
    {
        var overlay = MapHealth(status.Health);

        var displayLabel = status.Label;
        var truncated = false;
        if (maxLabelWidth > 0f && !string.IsNullOrEmpty(status.Label))
        {
            displayLabel = TruncateToFit(status.Label, maxLabelWidth, out truncated);
        }

        StatusPill.Render(
            overlay,
            elapsed,
            PillTransition.None,
            secondsSinceTransition: 0f,
            lastError: status.Detail,
            style: style ?? StatusPillStyle.Broadcast,
            overrideLabel: displayLabel
        );

        // StatusPill only tooltips on Failed via lastError. Surface Detail for
        // other states (Degraded warning, Unknown "why?") too, and if the label
        // was truncated, always include the full text so the user can recover it.
        if (overlay == OverlayStatus.Failed || !ImGui.IsItemHovered())
        {
            return;
        }

        if (truncated)
        {
            var tooltip = string.IsNullOrEmpty(status.Detail)
                ? status.Label
                : $"{status.Label}\n\n{status.Detail}";
            ImGui.SetTooltip(tooltip);
        }
        else if (!string.IsNullOrEmpty(status.Detail))
        {
            ImGui.SetTooltip(status.Detail);
        }
    }

    /// <summary>
    ///     Returns the label string that <see cref="Render"/> would actually draw given
    ///     the same <paramref name="maxLabelWidth"/>. Callers use this to measure the
    ///     chip's true width for right-alignment — otherwise the post-truncation label
    ///     is typically shorter than the budget and the chip floats away from the edge.
    /// </summary>
    public static string TruncateLabel(string label, float maxLabelWidth)
    {
        if (string.IsNullOrEmpty(label) || maxLabelWidth <= 0f)
        {
            return label;
        }

        return TruncateToFit(label, maxLabelWidth, out _);
    }

    /// <summary>
    ///     Trims <paramref name="text"/> from the right, appending "…", until the
    ///     result fits within <paramref name="maxPx"/>. Linear scan — fine for
    ///     short status labels where binary search would be overkill.
    /// </summary>
    private static string TruncateToFit(string text, float maxPx, out bool wasTruncated)
    {
        wasTruncated = false;
        if (ImGui.CalcTextSize(text).X <= maxPx)
        {
            return text;
        }

        wasTruncated = true;
        const string ellipsis = "…";

        // Even "…" alone doesn't fit — return it anyway so the user sees *something*
        // rather than an empty chip. The caller's layout math will just be slightly tight.
        if (ImGui.CalcTextSize(ellipsis).X >= maxPx)
        {
            return ellipsis;
        }

        for (var len = text.Length - 1; len > 0; len--)
        {
            var candidate = text[..len].TrimEnd() + ellipsis;
            if (ImGui.CalcTextSize(candidate).X <= maxPx)
            {
                return candidate;
            }
        }

        return ellipsis;
    }
}
