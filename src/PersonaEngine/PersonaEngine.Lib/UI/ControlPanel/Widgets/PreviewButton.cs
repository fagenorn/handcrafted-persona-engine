using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

public static partial class ImGuiHelpers
{
    public enum PreviewButtonState
    {
        /// <summary>No preview playing — show play triangle, clickable.</summary>
        Idle,

        /// <summary>This button's preview is playing — show stop square, clickable, breathing pulse.</summary>
        Playing,

        /// <summary>Another button's preview is playing — show play triangle, greyed out, not clickable.</summary>
        Disabled,
    }

    public enum PreviewButtonSize
    {
        Compact = 22,
        Standard = 28,
    }

    /// <summary>
    ///     Media-style preview button with draw-list shapes (no font glyph dependency).
    ///     <list type="bullet">
    ///         <item><see cref="PreviewButtonState.Idle" /> — play triangle, accent fill, clickable.</item>
    ///         <item><see cref="PreviewButtonState.Playing" /> — stop square, breathing accent pulse, clickable (stops playback).</item>
    ///         <item><see cref="PreviewButtonState.Disabled" /> — play triangle, muted fill, not clickable.</item>
    ///     </list>
    /// </summary>
    /// <returns><see langword="true"/> if clicked this frame (only when <paramref name="state"/> is not <see cref="PreviewButtonState.Disabled"/>).</returns>
    public static bool PreviewButton(
        string id,
        PreviewButtonState state,
        float elapsed,
        PreviewButtonSize size = PreviewButtonSize.Standard
    )
    {
        var px = (float)size;
        var cursor = ImGui.GetCursorScreenPos();
        var btnSize = new Vector2(px, px);

        // Hit-testing — disabled buttons register a dummy so layout advances, but don't click.
        bool clicked;
        if (state == PreviewButtonState.Disabled)
        {
            ImGui.Dummy(btnSize);
            clicked = false;
        }
        else
        {
            clicked = ImGui.InvisibleButton(id, btnSize);
            HandCursorOnHover();
        }

        var hovered = state != PreviewButtonState.Disabled && ImGui.IsItemHovered();
        var active = state != PreviewButtonState.Disabled && ImGui.IsItemActive();

        var drawList = ImGui.GetWindowDrawList();
        var center = new Vector2(cursor.X + px * 0.5f, cursor.Y + px * 0.5f);
        var radius = px * 0.5f;

        // ── Background circle ────────────────────────────────────────────────
        float bgAlpha;
        if (state == PreviewButtonState.Disabled)
        {
            bgAlpha = 0.15f;
        }
        else if (state == PreviewButtonState.Playing)
        {
            // Breathing pulse: alpha oscillates between 0.5 and 0.75
            bgAlpha = 0.5f + 0.25f * MathF.Sin(elapsed * MathF.PI * 2f * 0.8f);
            bgAlpha =
                active ? 1.0f
                : hovered ? MathF.Max(bgAlpha, 0.85f)
                : bgAlpha;
        }
        else
        {
            bgAlpha =
                active ? 1.0f
                : hovered ? 0.85f
                : 0.6f;
        }

        var bgColor = Theme.AccentPrimary with { W = bgAlpha };
        ImGui.AddCircleFilled(drawList, center, radius, ImGui.ColorConvertFloat4ToU32(bgColor));

        // ── Icon shape ───────────────────────────────────────────────────────
        var iconColor = state == PreviewButtonState.Disabled ? Theme.TextTertiary : Theme.Base;
        var iconCol = ImGui.ColorConvertFloat4ToU32(iconColor);

        if (state == PreviewButtonState.Playing)
        {
            // Stop square — centered, ~40% of button diameter
            var halfSq = px * 0.2f;
            ImGui.AddRectFilled(
                drawList,
                new Vector2(center.X - halfSq, center.Y - halfSq),
                new Vector2(center.X + halfSq, center.Y + halfSq),
                iconCol,
                2f // slight rounding on the stop square
            );
        }
        else
        {
            // Play triangle — right-pointing, sized to leave a visible gap inside the
            // circle. Uses PathLineTo + PathFillConvex instead of AddTriangleFilled
            // because the path renderer produces cleaner anti-aliasing at the acute
            // right tip (AddTriangleFilled's AA fringe overlaps at sharp vertices).
            var triH = px * 0.24f;
            var triW = px * 0.28f;
            var cx = center.X + radius * 0.08f;
            ImGui.PathLineTo(drawList, new Vector2(cx - triW * 0.45f, center.Y - triH));
            ImGui.PathLineTo(drawList, new Vector2(cx + triW * 0.55f, center.Y));
            ImGui.PathLineTo(drawList, new Vector2(cx - triW * 0.45f, center.Y + triH));
            ImGui.PathFillConvex(drawList, iconCol);
        }

        // Tooltip
        if (hovered)
        {
            var tip = state == PreviewButtonState.Playing ? "Stop" : "Preview";
            Tooltip(tip);
        }

        return clicked;
    }
}
