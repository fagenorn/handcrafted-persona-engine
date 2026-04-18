using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

public static partial class ImGuiHelpers
{
    /// <summary>
    ///     Renders a visually distinct section header with an accent-colored label
    ///     and a soft gradient divider.
    /// </summary>
    public static void SectionHeader(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.AccentPrimary);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();

        // Gradient divider: fades from transparent → accent → transparent
        var cursor = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var availW = ImGui.GetContentRegionAvail().X;
        var dividerW = availW * 0.6f;
        var startX = cursor.X + (availW - dividerW) * 0.5f;
        var centerX = startX + dividerW * 0.5f;
        var y = cursor.Y;

        var transparent = ImGui.ColorConvertFloat4ToU32(Theme.AccentPrimary with { W = 0f });
        var accent = ImGui.ColorConvertFloat4ToU32(Theme.AccentPrimary with { W = 0.15f });

        // Left half: transparent → accent
        ImGui.AddRectFilledMultiColor(
            drawList,
            new Vector2(startX, y),
            new Vector2(centerX, y + 1f),
            transparent,
            accent,
            accent,
            transparent
        );

        // Right half: accent → transparent
        ImGui.AddRectFilledMultiColor(
            drawList,
            new Vector2(centerX, y),
            new Vector2(startX + dividerW, y + 1f),
            accent,
            transparent,
            transparent,
            accent
        );

        ImGui.Dummy(new Vector2(0f, 2f));
    }
}
