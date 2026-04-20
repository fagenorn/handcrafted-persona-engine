using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

public static partial class ImGuiHelpers
{
    /// <summary>
    ///     Draws a filled colored circle and advances the layout cursor by the dot's bounding box.
    ///     Optionally renders a larger glow halo behind the dot.
    /// </summary>
    /// <param name="color">The fill color for the dot.</param>
    /// <param name="radius">The circle radius in pixels (default 5).</param>
    /// <param name="glowAlpha">Glow halo opacity (0 = no glow). Halo uses the same color at this alpha.</param>
    public static void StatusDot(Vector4 color, float radius = 5f, float glowAlpha = 0f)
    {
        const float glowScale = 2.4f;

        var drawList = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();
        var textH = ImGui.GetTextLineHeight();
        var center = new Vector2(cursor.X + radius, cursor.Y + textH * 0.5f);

        // Optional pulsing glow halo behind the dot
        if (glowAlpha > 0f)
        {
            var glowColor = color with { W = glowAlpha };
            var glowCol = ImGui.ColorConvertFloat4ToU32(glowColor);
            ImGui.AddCircleFilled(drawList, center, radius * glowScale, glowCol);
        }

        var col = ImGui.ColorConvertFloat4ToU32(color);
        ImGui.AddCircleFilled(drawList, center, radius, col);
        ImGui.Dummy(new Vector2(radius * 2f, textH));
    }
}
