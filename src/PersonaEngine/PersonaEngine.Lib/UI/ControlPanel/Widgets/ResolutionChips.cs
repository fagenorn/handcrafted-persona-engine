using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

public static partial class ImGuiHelpers
{
    private enum ResolutionOrientation
    {
        Landscape,
        Portrait,
        Square,
    }

    private static readonly (int Width, int Height, string Label)[] LandscapeChipPresets =
    [
        (1280, 720, "720p"),
        (1920, 1080, "1080p"),
        (2560, 1440, "1440p"),
        (3840, 2160, "4K"),
    ];

    private static readonly (int Width, int Height, string Label)[] PortraitChipPresets =
    [
        (720, 1280, "720×1280"),
        (1080, 1920, "1080×1920"),
        (1440, 2560, "1440×2560"),
        (2160, 3840, "2160×3840"),
    ];

    private static readonly (int Width, int Height, string Label)[] SquareChipPresets =
    [
        (720, 720, "720²"),
        (1080, 1080, "1080²"),
        (1440, 1440, "1440²"),
        (2160, 2160, "2160²"),
    ];

    /// <summary>
    ///     Stream-friendly preset chips: orientation on top (Landscape / Portrait /
    ///     Square), then four size chips for the chosen orientation. Targets VTuber
    ///     canvas sizes — 720p / 1080p / 1440p / 4K and their portrait + square siblings —
    ///     rather than every monitor preset under the sun.
    ///     <para>
    ///         When the user flips orientation, the size is re-picked from the new
    ///         preset list by nearest total pixel count, so a 1920×1080 → Portrait
    ///         click lands on 1080×1920 instead of the list's first entry.
    ///     </para>
    ///     Returns <see langword="true"/> when the value changed.
    /// </summary>
    public static bool ResolutionChips(string id, ref int width, ref int height)
    {
        const float chipGap = 6f;

        var orientation = ClassifyOrientation(width, height);
        var changed = false;

        // Orientation row
        if (Chip($"Landscape##{id}_land", orientation == ResolutionOrientation.Landscape))
        {
            if (orientation != ResolutionOrientation.Landscape)
            {
                (width, height) = PickNearestPreset(LandscapeChipPresets, width, height);
                orientation = ResolutionOrientation.Landscape;
                changed = true;
            }
        }

        ImGui.SameLine(0f, chipGap);

        if (Chip($"Portrait##{id}_port", orientation == ResolutionOrientation.Portrait))
        {
            if (orientation != ResolutionOrientation.Portrait)
            {
                (width, height) = PickNearestPreset(PortraitChipPresets, width, height);
                orientation = ResolutionOrientation.Portrait;
                changed = true;
            }
        }

        ImGui.SameLine(0f, chipGap);

        if (Chip($"Square##{id}_sq", orientation == ResolutionOrientation.Square))
        {
            if (orientation != ResolutionOrientation.Square)
            {
                (width, height) = PickNearestPreset(SquareChipPresets, width, height);
                orientation = ResolutionOrientation.Square;
                changed = true;
            }
        }

        ImGui.Spacing();

        // Size row — four chips scoped to the current orientation.
        var presets = orientation switch
        {
            ResolutionOrientation.Portrait => PortraitChipPresets,
            ResolutionOrientation.Square => SquareChipPresets,
            _ => LandscapeChipPresets,
        };

        for (var i = 0; i < presets.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0f, chipGap);

            var preset = presets[i];
            var selected = width == preset.Width && height == preset.Height;
            if (Chip($"{preset.Label}##{id}_sz{i}", selected))
            {
                if (!selected)
                {
                    width = preset.Width;
                    height = preset.Height;
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static ResolutionOrientation ClassifyOrientation(int w, int h) =>
        w == h ? ResolutionOrientation.Square
        : h > w ? ResolutionOrientation.Portrait
        : ResolutionOrientation.Landscape;

    private static (int Width, int Height) PickNearestPreset(
        (int Width, int Height, string Label)[] presets,
        int currentWidth,
        int currentHeight
    )
    {
        // Nearest by total pixel count — feels natural when flipping 1080p ↔ 1080×1920.
        var targetPixels = (long)currentWidth * currentHeight;
        var best = presets[0];
        var bestDiff = long.MaxValue;

        foreach (var p in presets)
        {
            var diff = Math.Abs((long)p.Width * p.Height - targetPixels);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = p;
            }
        }

        return (best.Width, best.Height);
    }
}
