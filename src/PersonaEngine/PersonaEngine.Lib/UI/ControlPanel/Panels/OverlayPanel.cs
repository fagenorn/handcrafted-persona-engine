using System.Numerics;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.Overlay;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels;

/// <summary>
///     Top-level panel for the floating desktop overlay. The overlay always
///     mirrors the Live2D render target; the user can enable/disable, see live
///     status, and reset its position from here.
/// </summary>
public sealed class OverlayPanel(OverlayHost host, IOptionsMonitor<AvatarAppConfig> options)
{
    // Cache for the "Position: X, Y    Size: W × H" line. Rebuilds only when
    // any of the four ints change (i.e., when the user drags or resizes the
    // overlay), skipping the per-frame string interpolation the rest of the
    // time.
    private int _cachedX = int.MinValue;
    private int _cachedY;
    private int _cachedW;
    private int _cachedH;
    private string _cachedGeometryText = string.Empty;

    public void Render(float deltaTime)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(
            "An always-on-top transparent window that mirrors the avatar and subtitles "
                + "directly on your desktop — no OBS or window chrome. Hover it to reveal a "
                + "thin border with grab handles for moving and resizing."
        );
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        ImGui.Dummy(new Vector2(0f, 8f));

        RenderEnableRow();
        RenderGeometryRow();
    }

    private void RenderEnableRow()
    {
        ImGuiHelpers.SettingLabel(
            "Enabled",
            "Show the floating overlay on your desktop. Clicking retries if the last start failed."
        );

        var desired = host.DesiredEnabled;

        // Failed-state click-to-retry: clicking the checkbox from Failed is
        // equivalent to SetEnabled(true) — the state machine permits TurnOn
        // from Failed, which restarts the thread. Live status and the
        // retry affordance are surfaced in the Dashboard's presence strip;
        // this row only owns the on/off write.
        if (ImGui.Checkbox("##OverlayEnabled", ref desired))
        {
            host.SetEnabled(desired);
        }
    }

    private void RenderGeometryRow()
    {
        var overlay = options.CurrentValue.Overlay;

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted(GetGeometryText(overlay.X, overlay.Y, overlay.Width, overlay.Height));
        ImGui.TextUnformatted("(drag the overlay's border to reposition or resize)");
        ImGui.PopStyleColor();

        ImGui.Dummy(new Vector2(0f, 6f));

        if (ImGui.Button("Reset position"))
        {
            host.ResetPosition();
        }
        ImGuiHelpers.HandCursorOnHover();

        ImGui.SameLine(0f, 6f);

        if (ImGui.Button("Reset size"))
        {
            host.ResetSize();
        }
        ImGuiHelpers.HandCursorOnHover();
    }

    private string GetGeometryText(int x, int y, int w, int h)
    {
        if (x == _cachedX && y == _cachedY && w == _cachedW && h == _cachedH)
        {
            return _cachedGeometryText;
        }

        _cachedX = x;
        _cachedY = y;
        _cachedW = w;
        _cachedH = h;
        _cachedGeometryText = $"Position: {x}, {y}    Size: {w} \u00d7 {h}";
        return _cachedGeometryText;
    }
}
