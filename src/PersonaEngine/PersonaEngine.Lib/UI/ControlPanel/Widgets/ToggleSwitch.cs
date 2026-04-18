using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

public static partial class ImGuiHelpers
{
    /// <summary>
    ///     Renders a custom pill-shaped toggle switch with a springy animated knob and ripple effect.
    /// </summary>
    /// <param name="id">ImGui widget ID string.</param>
    /// <param name="value">Current toggle state; toggled on click.</param>
    /// <param name="knobPosition">
    ///     Persistent <see cref="AnimatedFloat"/> (0 = off, 1 = on) owned by the caller.
    ///     Updated each frame.
    /// </param>
    /// <param name="dt">Frame delta time in seconds for animation smoothing.</param>
    /// <returns><see langword="true"/> if the value changed this frame.</returns>
    public static bool ToggleSwitch(
        string id,
        ref bool value,
        ref AnimatedFloat knobPosition,
        float dt
    )
    {
        const float trackW = 40f;
        const float trackH = 20f;
        const float knobRadius = 8f;
        const float trackRounding = trackH * 0.5f;

        // Drive animation toward current logical state
        knobPosition.Target = value ? 1f : 0f;
        knobPosition.Update(dt);

        // Center the toggle vertically within the standard frame height so it aligns
        // with labels that use AlignTextToFramePadding.
        var frameH = ImGui.GetFrameHeight();
        if (frameH > trackH)
        {
            var offset = (frameH - trackH) * 0.5f;
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offset);
        }

        var cursor = ImGui.GetCursorScreenPos();
        var changed = false;

        if (ImGui.InvisibleButton(id, new Vector2(trackW, trackH)))
        {
            value = !value;
            changed = true;
        }

        HandCursorOnHover();

        var t = knobPosition.Current;

        // Apply overshoot easing for springy feel — only when traveling forward
        var clampedT = Math.Clamp(t, 0f, 1f);
        var easedT = Math.Clamp(Easing.EaseOutBack(clampedT), 0f, 1.15f);

        var trackColor = LerpColor(Theme.Surface2, Theme.AccentPrimary, clampedT);
        var trackCol = ImGui.ColorConvertFloat4ToU32(trackColor);
        var knobCol = ImGui.ColorConvertFloat4ToU32(Theme.TextPrimary);

        var drawList = ImGui.GetWindowDrawList();
        var trackMin = cursor;
        var trackMax = new Vector2(cursor.X + trackW, cursor.Y + trackH);

        ImGui.AddRectFilled(drawList, trackMin, trackMax, trackCol, trackRounding);

        var travel = trackW - (knobRadius + 2f) * 2f;
        var knobX = cursor.X + knobRadius + 2f + travel * Math.Clamp(easedT, 0f, 1f);
        var knobY = cursor.Y + trackH * 0.5f;
        var knobCenter = new Vector2(knobX, knobY);

        // Ripple effect: expanding ring that fades out as animation settles
        if (!knobPosition.IsSettled)
        {
            var rippleProgress = 1f - MathF.Abs(t - knobPosition.Target);
            if (rippleProgress > 0f && rippleProgress < 0.8f)
            {
                var rippleRadius = knobRadius + 6f * (1f - rippleProgress);
                var rippleAlpha = rippleProgress * 0.2f;
                var rippleCol = ImGui.ColorConvertFloat4ToU32(
                    Theme.AccentPrimary with
                    {
                        W = rippleAlpha,
                    }
                );
                ImGui.AddCircle(drawList, knobCenter, rippleRadius, rippleCol, 0, 1.5f);
            }
        }

        ImGui.AddCircleFilled(drawList, knobCenter, knobRadius, knobCol);

        return changed;
    }
}
