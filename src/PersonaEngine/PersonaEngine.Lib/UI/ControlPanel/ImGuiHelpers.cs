using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
///     Shared zero-allocation rendering helpers for the control panel.
/// </summary>
public static partial class ImGuiHelpers
{
    /// <summary>
    ///     Sets the mouse cursor to <see cref="ImGuiMouseCursor.Hand"/> when the
    ///     previous item is hovered. Call immediately after any clickable widget.
    /// </summary>
    public static void HandCursorOnHover()
    {
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
    }

    /// <summary>
    ///     Renders a hover tooltip on the previous item using a short delay.
    /// </summary>
    public static void Tooltip(string text)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            return;

        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

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

        ImGui.AddRectFilledMultiColor(
            drawList,
            new Vector2(startX, y),
            new Vector2(centerX, y + 1f),
            transparent,
            accent,
            accent,
            transparent
        );

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

    /// <summary>
    ///     Persistent state for an animated collapsible section.
    /// </summary>
    public sealed class CollapsibleState
    {
        internal bool IsOpen;
        internal bool Initialized;
        internal AnimatedFloat HeightAnim = new(0f);
        internal float ContentHeight;
    }

    /// <summary>
    ///     Renders a collapsing header with animated expand/collapse.
    /// </summary>
    public static void CollapsibleSection(
        string label,
        string? subtitle,
        bool defaultOpen,
        Action renderContent,
        string? hint = null,
        CollapsibleState? animState = null,
        float dt = 0f
    )
    {
        ImGui.SetNextItemOpen(defaultOpen, ImGuiCond.FirstUseEver);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.AccentPrimary);
        var headerOpen = ImGui.CollapsingHeader($"##{label}_header");
        ImGui.PopStyleColor();
        HandCursorOnHover();

        RenderCollapsibleHeaderText(label, hint);

        if (subtitle is not null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted(subtitle);
            ImGui.PopStyleColor();
        }

        if (animState is null)
        {
            if (headerOpen)
                renderContent();
            return;
        }

        if (!animState.Initialized)
        {
            animState.IsOpen = defaultOpen;
            animState.HeightAnim = new AnimatedFloat(defaultOpen ? 1f : 0f);
            animState.Initialized = true;
        }

        if (headerOpen != animState.IsOpen)
            animState.IsOpen = headerOpen;

        animState.HeightAnim.Target = animState.IsOpen ? 1f : 0f;
        animState.HeightAnim.Update(dt);

        var t = Math.Clamp(animState.HeightAnim.Current, 0f, 1f);

        if (t > 0.001f || animState.IsOpen)
        {
            var clipHeight = animState.ContentHeight * t;
            var cursorStart = ImGui.GetCursorScreenPos();

            var drawList = ImGui.GetWindowDrawList();
            ImGui.PushClipRect(
                cursorStart,
                new Vector2(
                    cursorStart.X + ImGui.GetContentRegionAvail().X + 100f,
                    cursorStart.Y + clipHeight
                ),
                true
            );

            var contentStartY = ImGui.GetCursorPosY();
            renderContent();
            var contentEndY = ImGui.GetCursorPosY();

            ImGui.PopClipRect();

            animState.ContentHeight = contentEndY - contentStartY;

            ImGui.SetCursorPosY(contentStartY + clipHeight);
            ImGui.Dummy(new Vector2(0f, 0f));
        }
    }

    private static void RenderCollapsibleHeaderText(string label, string? hint)
    {
        var headerMin = ImGui.GetItemRectMin();
        var headerHeight = ImGui.GetItemRectSize().Y;
        var drawList = ImGui.GetWindowDrawList();
        var framePad = ImGui.GetStyle().FramePadding;
        var fontSize = ImGui.GetFontSize();
        var arrowWidth = fontSize + framePad.X;

        var labelX = headerMin.X + arrowWidth + 8f;
        var labelY = headerMin.Y + (headerHeight - fontSize) * 0.5f;
        drawList.AddText(
            new Vector2(labelX, labelY),
            ImGui.ColorConvertFloat4ToU32(Theme.AccentPrimary),
            label
        );

        if (hint is not null)
        {
            var labelWidth = ImGui.CalcTextSize(label).X;
            var hintScale = 0.82f;
            var hintFont = ImGui.GetFont();
            var hintFontSize = fontSize * hintScale;
            var hintX = labelX + labelWidth + 12f;
            var hintY = labelY + fontSize - hintFontSize;
            unsafe
            {
                drawList.AddText(
                    hintFont,
                    hintFontSize,
                    new Vector2(hintX, hintY),
                    ImGui.ColorConvertFloat4ToU32(Theme.TextTertiary),
                    hint
                );
            }
        }
    }

    /// <summary>
    ///     Draws a filled colored circle and advances the layout cursor.
    /// </summary>
    public static void StatusDot(Vector4 color, float radius = 5f, float glowAlpha = 0f)
    {
        const float glowScale = 2.4f;

        var drawList = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();
        var textH = ImGui.GetTextLineHeight();
        var center = new Vector2(cursor.X + radius, cursor.Y + textH * 0.5f);

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

    public static float SettingRowHeight => ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y;

    public static void SettingLabel(string label, string? tooltip, float? labelWidth = null)
    {
        var width = labelWidth ?? Math.Clamp(ImGui.GetContentRegionAvail().X * 0.30f, 130f, 240f);

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

    public static void SettingEndRow(float rowStartY)
    {
        var elapsed = ImGui.GetCursorPosY() - rowStartY;
        var minHeight = SettingRowHeight;
        if (elapsed < minHeight)
            ImGui.Dummy(new Vector2(0f, minHeight - elapsed));
    }

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

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted(leftLabel);
        ImGui.PopStyleColor();
        ImGui.SameLine();

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

    internal static bool ScannedCombo(
        string id,
        ScannedNamePicker picker,
        string savedName,
        out string selected,
        Action onRefresh,
        string refreshTooltip,
        float refreshButtonWidth = 100f
    )
    {
        selected = savedName;
        var changed = false;

        var selectedIndex = picker.IndexOf(savedName);

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - (refreshButtonWidth + 10f));

        if (picker.Choices.Count > 0)
        {
            var choices = picker.Choices is string[] arr ? arr : picker.Choices.ToArray();
            if (ImGui.Combo($"##{id}", ref selectedIndex, choices, choices.Length))
            {
                var stripped = ScannedNamePicker.StripSuffix(choices[selectedIndex]);
                if (!string.Equals(stripped, savedName, StringComparison.Ordinal))
                {
                    selected = stripped;
                    changed = true;
                }
            }
        }
        else
        {
            ImGui.BeginDisabled();
            var empty = 0;
            var placeholder = new[] { "(none)" };
            ImGui.Combo($"##{id}", ref empty, placeholder, placeholder.Length);
            ImGui.EndDisabled();
        }

        HandCursorOnHover();
        ImGui.SameLine();

        if (ImGui.Button($"Refresh##{id}", new Vector2(refreshButtonWidth, 0f)))
        {
            onRefresh();
        }

        HandCursorOnHover();
        Tooltip(refreshTooltip);

        return changed;
    }

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

    public static bool ResolutionChips(string id, ref int width, ref int height)
    {
        const float chipGap = 6f;

        var orientation = ClassifyOrientation(width, height);
        var changed = false;

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

    private enum ResolutionOrientation
    {
        Landscape,
        Portrait,
        Square,
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

    internal static Vector4 LerpColor(Vector4 a, Vector4 b, float t) =>
        new(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t,
            a.W + (b.W - a.W) * t
        );
}
