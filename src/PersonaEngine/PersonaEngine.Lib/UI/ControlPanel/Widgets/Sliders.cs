using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

public static partial class ImGuiHelpers
{
    /// <summary>
    ///     Draws a subtle accent glow rect behind the previous slider while it is hovered or
    ///     actively being dragged. Stateless — no shared static alpha field, which previously
    ///     caused all sliders to light up together when any one was hovered.
    ///     The <paramref name="dt" /> parameter is kept for API compatibility but is unused.
    /// </summary>
    public static void SliderGlow(float dt = 0f)
    {
        if (!ImGui.IsItemHovered() && !ImGui.IsItemActive())
            return;

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var glowColor = Theme.AccentPrimary with { W = 0.12f };
        var col = ImGui.ColorConvertFloat4ToU32(glowColor);
        var drawList = ImGui.GetWindowDrawList();
        ImGui.AddRectFilled(drawList, min, max, col, ImGui.GetStyle().FrameRounding);
    }

    /// <summary>
    ///     Float slider with perceptual labels flanking it and a small muted badge showing the
    ///     raw numeric value on the right. Calls <see cref="SliderGlow" /> after the widget.
    /// </summary>
    /// <returns><see langword="true"/> if the value changed this frame.</returns>
    public static bool LabeledSlider(
        string id,
        ref float value,
        float min,
        float max,
        string leftLabel,
        string rightLabel,
        string format = "%.2f",
        float dt = 0f
    )
    {
        var avail = ImGui.GetContentRegionAvail();

        // Left perceptual label
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted(leftLabel);
        ImGui.PopStyleColor();
        ImGui.SameLine();

        // Slider fills the middle, leaving room for the right label + badge (~140px)
        var sliderWidth = Math.Max(100f, avail.X - ImGui.CalcTextSize(leftLabel).X - 140f);
        ImGui.SetNextItemWidth(sliderWidth);
        var changed = ImGui.SliderFloat(id, ref value, min, max, format);
        SliderGlow(dt);
        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted(rightLabel);
        ImGui.PopStyleColor();

        return changed;
    }

    /// <summary>
    ///     Int variant of <see cref="LabeledSlider(string, ref float, float, float, string, string, string, float)" />.
    /// </summary>
    public static bool LabeledSlider(
        string id,
        ref int value,
        int min,
        int max,
        string leftLabel,
        string rightLabel,
        float dt = 0f
    )
    {
        var avail = ImGui.GetContentRegionAvail();

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted(leftLabel);
        ImGui.PopStyleColor();
        ImGui.SameLine();

        var sliderWidth = Math.Max(100f, avail.X - ImGui.CalcTextSize(leftLabel).X - 140f);
        ImGui.SetNextItemWidth(sliderWidth);
        var changed = ImGui.SliderInt(id, ref value, min, max);
        SliderGlow(dt);
        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted(rightLabel);
        ImGui.PopStyleColor();

        return changed;
    }
}
