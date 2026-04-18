using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

public static partial class ImGuiHelpers
{
    private static readonly (int Width, int Height, string Label)[] LandscapeResolutions =
    [
        (640, 480, "640 × 480 (SD)"),
        (800, 600, "800 × 600 (SVGA)"),
        (1024, 768, "1024 × 768 (XGA)"),
        (1280, 720, "1280 × 720 (HD)"),
        (1280, 1024, "1280 × 1024 (SXGA)"),
        (1366, 768, "1366 × 768 (HD)"),
        (1600, 900, "1600 × 900 (HD+)"),
        (1920, 1080, "1920 × 1080 (Full HD)"),
        (2560, 1440, "2560 × 1440 (QHD)"),
        (3840, 2160, "3840 × 2160 (4K)"),
    ];

    private static readonly string[] LandscapeLabels = LandscapeResolutions
        .Select(r => r.Label)
        .ToArray();

    private static readonly (int Width, int Height, string Label)[] PortraitResolutions =
        LandscapeResolutions
            .Select(r =>
            {
                var parenIdx = r.Label.IndexOf('(');
                var suffix = parenIdx >= 0 ? " " + r.Label[parenIdx..] : "";
                return (r.Height, r.Width, $"{r.Height} × {r.Width}{suffix}");
            })
            .ToArray();

    private static readonly string[] PortraitLabels = PortraitResolutions
        .Select(r => r.Label)
        .ToArray();

    private static readonly (int Width, int Height, string Label)[] SquareResolutions =
    [
        (256, 256, "256 × 256"),
        (512, 512, "512 × 512"),
        (720, 720, "720 × 720"),
        (1024, 1024, "1024 × 1024"),
        (1080, 1080, "1080 × 1080"),
        (1440, 1440, "1440 × 1440"),
        (2160, 2160, "2160 × 2160"),
    ];

    private static readonly string[] SquareLabels = SquareResolutions
        .Select(r => r.Label)
        .ToArray();

    /// <summary>
    ///     Renders a resolution combo with common presets.
    ///     Non-square pickers include a landscape/portrait orientation toggle.
    ///     Returns <see langword="true"/> when the value changed.
    /// </summary>
    public static bool ResolutionPicker(
        string id,
        ref int width,
        ref int height,
        bool square = false
    )
    {
        if (square)
        {
            return ResolutionCombo(id, ref width, ref height, SquareResolutions, SquareLabels);
        }

        var changed = false;
        var isPortrait = height > width;

        if (ImGui.RadioButton($"Landscape##{id}", !isPortrait))
        {
            if (isPortrait)
            {
                (width, height) = (height, width);
                isPortrait = false;
                changed = true;
            }
        }

        HandCursorOnHover();
        ImGui.SameLine();

        if (ImGui.RadioButton($"Portrait##{id}", isPortrait))
        {
            if (!isPortrait)
            {
                (width, height) = (height, width);
                isPortrait = true;
                changed = true;
            }
        }

        HandCursorOnHover();

        var presets = isPortrait ? PortraitResolutions : LandscapeResolutions;
        var labels = isPortrait ? PortraitLabels : LandscapeLabels;

        changed |= ResolutionCombo(id, ref width, ref height, presets, labels);

        return changed;
    }

    private static bool ResolutionCombo(
        string id,
        ref int width,
        ref int height,
        (int Width, int Height, string Label)[] presets,
        string[] labels
    )
    {
        var currentIndex = -1;
        for (var i = 0; i < presets.Length; i++)
        {
            if (presets[i].Width == width && presets[i].Height == height)
            {
                currentIndex = i;
                break;
            }
        }

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.Combo($"##{id}", ref currentIndex, labels, labels.Length))
        {
            width = presets[currentIndex].Width;
            height = presets[currentIndex].Height;
            HandCursorOnHover();
            return true;
        }

        HandCursorOnHover();
        return false;
    }
}
