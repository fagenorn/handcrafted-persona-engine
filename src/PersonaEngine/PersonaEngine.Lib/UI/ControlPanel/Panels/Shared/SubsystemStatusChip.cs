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
    /// <param name="style">
    ///     Optional style override; defaults to <see cref="StatusPillStyle.Broadcast" />.
    /// </param>
    public static void Render(SubsystemStatus status, StatusPillStyle? style = null)
    {
        var overlay = MapHealth(status.Health);
        var elapsed = (float)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
        StatusPill.Render(
            overlay,
            elapsed,
            PillTransition.None,
            secondsSinceTransition: 0f,
            lastError: status.Detail,
            style: style ?? StatusPillStyle.Broadcast,
            overrideLabel: status.Label
        );

        // StatusPill only tooltips on Failed via lastError. Surface Detail for
        // other states (Degraded warning, Unknown "why?") too.
        if (
            overlay != OverlayStatus.Failed
            && !string.IsNullOrEmpty(status.Detail)
            && ImGui.IsItemHovered()
        )
        {
            ImGui.SetTooltip(status.Detail);
        }
    }
}
