using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

public static partial class ImGuiHelpers
{
    /// <summary>
    ///     Standard row height for a setting (label + widget). Widgets shorter than this
    ///     (e.g., ToggleSwitch at 20px) get padded so all settings have consistent spacing.
    ///     Computed from ImGui's standard framed-widget height (FramePadding.Y * 2 + FontSize).
    /// </summary>
    public static float SettingRowHeight => ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y;

    /// <summary>
    ///     Renders a left-aligned label with an optional inline (?) tooltip marker,
    ///     then positions the cursor at a proportional offset for the following widget.
    /// </summary>
    /// <param name="label">The label text.</param>
    /// <param name="tooltip">Optional tooltip shown when hovering the (?) marker.</param>
    /// <param name="labelWidth">
    ///     Explicit horizontal offset. When <see langword="null"/>, calculated as 30% of
    ///     available width clamped to [130, 240].
    /// </param>
    public static void SettingLabel(string label, string? tooltip, float? labelWidth = null)
    {
        var width = labelWidth ?? Math.Clamp(ImGui.GetContentRegionAvail().X * 0.30f, 130f, 240f);

        // Align plain text vertically to the center of framed widgets (Combo, Slider, etc.)
        // so label and widget share the same visual midline.
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);

        if (tooltip is not null)
        {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted("(?)");
            ImGui.PopStyleColor();
            Tooltip(tooltip);
        }

        ImGui.SameLine(width);
        ImGui.SetNextItemWidth(-1f);
    }

    /// <summary>
    ///     Call after each setting widget (toggle, combo, slider, etc.) to ensure the row
    ///     occupies at least <see cref="SettingRowHeight" /> pixels. This makes all settings
    ///     evenly spaced regardless of widget type.
    /// </summary>
    public static void SettingEndRow(float rowStartY)
    {
        var elapsed = ImGui.GetCursorPosY() - rowStartY;
        var minHeight = SettingRowHeight;
        if (elapsed < minHeight)
            ImGui.Dummy(new Vector2(0f, minHeight - elapsed));
    }
}
