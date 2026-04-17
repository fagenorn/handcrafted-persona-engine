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
    private float _elapsed;

    public void Render(float deltaTime)
    {
        _elapsed += deltaTime;

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
        var status = host.Status;

        // Failed-state click-to-retry: clicking the checkbox from Failed is
        // equivalent to SetEnabled(true) — the state machine permits
        // TurnOn from Failed, which restarts the thread.
        if (ImGui.Checkbox("##OverlayEnabled", ref desired))
        {
            host.SetEnabled(desired);
        }

        ImGui.SameLine(0f, 12f);
        StatusPill.Render(status, _elapsed, host.LastError);
    }

    private void RenderGeometryRow()
    {
        var overlay = options.CurrentValue.Overlay;

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted(
            $"Position: {overlay.X}, {overlay.Y}    Size: {overlay.Width} × {overlay.Height}"
        );
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
}
