using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

public static partial class ImGuiHelpers
{
    /// <summary>
    ///     Pill-shaped toggle rendered with <see cref="Theme.AccentPrimary" /> tint when selected
    ///     and <see cref="Theme.Surface2" /> when not. Used for filter/tag selectors.
    /// </summary>
    /// <returns><see langword="true"/> if clicked this frame.</returns>
    public static bool Chip(string label, bool selected, bool interactive = true)
    {
        var padding = new Vector2(10f, 4f);
        var rounding = 12f;

        // Match ImGui's native widget convention: everything after "##" is id-only
        // and is never rendered. Callers can pass "Landscape##Live2DRes_land" to
        // disambiguate two identically-labeled chips on the same screen.
        var hashIdx = label.IndexOf("##", StringComparison.Ordinal);
        var displayLabel = hashIdx >= 0 ? label[..hashIdx] : label;

        var textSize = ImGui.CalcTextSize(displayLabel);
        var size = new Vector2(textSize.X + padding.X * 2f, textSize.Y + padding.Y * 2f);

        var cursor = ImGui.GetCursorScreenPos();
        bool clicked;
        bool hovered;
        if (interactive)
        {
            clicked = ImGui.InvisibleButton($"##chip_{label}", size);
            HandCursorOnHover();
            hovered = ImGui.IsItemHovered();
        }
        else
        {
            ImGui.Dummy(size);
            clicked = false;
            hovered = false;
        }

        var drawList = ImGui.GetWindowDrawList();
        var min = cursor;
        var max = cursor + size;

        Vector4 fill =
            selected ? Theme.AccentPrimary with { W = 0.28f }
            : hovered ? Theme.SurfaceHover
            : Theme.Surface2;
        Vector4 textColor = selected ? Theme.AccentPrimary : Theme.TextPrimary;

        ImGui.AddRectFilled(drawList, min, max, ImGui.ColorConvertFloat4ToU32(fill), rounding);
        if (selected)
        {
            ImGui.AddRect(
                drawList,
                min,
                max,
                ImGui.ColorConvertFloat4ToU32(Theme.AccentPrimary with { W = 0.9f }),
                rounding,
                0,
                1.2f
            );
        }

        var textPos = new Vector2(min.X + padding.X, min.Y + padding.Y);
        ImGui
            .GetWindowDrawList()
            .AddText(textPos, ImGui.ColorConvertFloat4ToU32(textColor), displayLabel);

        return clicked;
    }
}
