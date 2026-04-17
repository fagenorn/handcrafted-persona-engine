using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.Overlay;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
///     Renders the "dot + label" status indicator used by the Overlay panel and
///     the Dashboard Overlay quick-toggle card. Pulses amber for transitional
///     states (Starting / Stopping) so the user can tell work is in flight.
/// </summary>
public static class StatusPill
{
    // Pulse frequency for transitional states. 1.2 Hz reads as a gentle heartbeat
    // without being distracting.
    private const float PulseHz = 1.2f;

    /// <summary>
    ///     Renders a status pill inline at the current cursor position.
    /// </summary>
    /// <param name="status">The overlay lifecycle status to render.</param>
    /// <param name="elapsed">
    ///     Accumulated seconds for the pulse animation — pass a monotonically
    ///     increasing float per owning panel (e.g. sum of deltaTimes).
    /// </param>
    /// <param name="lastError">Shown as a tooltip when status is Failed.</param>
    public static void Render(OverlayStatus status, float elapsed, string? lastError)
    {
        var (color, label, pulse) = status switch
        {
            OverlayStatus.Off => (Theme.TextTertiary, "Off", false),
            OverlayStatus.Starting => (Theme.Warning, "Starting…", true),
            OverlayStatus.Active => (Theme.Success, "Active", false),
            OverlayStatus.Stopping => (Theme.Warning, "Stopping…", true),
            OverlayStatus.Failed => (Theme.Error, "Failed — click to retry", false),
            _ => (Theme.TextTertiary, "?", false),
        };

        // Pulse dot alpha in [0.5, 1.0] @ PulseHz for transitional states.
        var dotColor = color;
        if (pulse)
        {
            var t = 0.75f + 0.25f * MathF.Sin(elapsed * 2f * MathF.PI * PulseHz);
            dotColor = color with { W = t };
        }

        ImGuiHelpers.StatusDot(dotColor);
        ImGui.SameLine(0f, 6f);

        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();

        if (status == OverlayStatus.Failed && !string.IsNullOrEmpty(lastError))
        {
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(lastError);
            }
        }
    }
}
