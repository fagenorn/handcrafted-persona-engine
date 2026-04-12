using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
///     Shared zero-allocation rendering helpers for the control panel.
/// </summary>
public static class ImGuiHelpers
{
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
    ///     Renders a visually distinct section header using an accent-colored separator.
    /// </summary>
    public static void SectionHeader(string label)
    {
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.AccentSecondary);
        ImGui.SeparatorText(label);
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    /// <summary>
    ///     Draws a filled colored circle and advances the layout cursor by the dot's bounding box.
    /// </summary>
    /// <param name="color">The fill color for the dot.</param>
    /// <param name="radius">The circle radius in pixels (default 5).</param>
    public static void StatusDot(Vector4 color, float radius = 5f)
    {
        var drawList = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();
        var center = new Vector2(cursor.X + radius, cursor.Y + radius);
        var col = ImGui.ColorConvertFloat4ToU32(color);

        ImGui.AddCircleFilled(drawList, center, radius, col);
        ImGui.Dummy(new Vector2(radius * 2f, radius * 2f));
    }

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
        var width = labelWidth
            ?? Math.Clamp(ImGui.GetContentRegionAvail().X * 0.30f, 130f, 240f);

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
    ///     Renders a custom pill-shaped toggle switch with a smooth animated knob.
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

        var cursor = ImGui.GetCursorScreenPos();
        var changed = false;

        // Invisible hit-test button that covers the track area
        if (ImGui.InvisibleButton(id, new Vector2(trackW, trackH)))
        {
            value = !value;
            changed = true;
        }

        // Lerp track color between Surface (off) and AccentPrimary (on)
        var t = knobPosition.Current;
        var trackColor = LerpColor(Theme.Surface, Theme.AccentPrimary, t);
        var trackCol = ImGui.ColorConvertFloat4ToU32(trackColor);
        var knobCol = ImGui.ColorConvertFloat4ToU32(Theme.TextPrimary);

        var drawList = ImGui.GetWindowDrawList();
        var trackMin = cursor;
        var trackMax = new Vector2(cursor.X + trackW, cursor.Y + trackH);

        // Draw rounded-rect track
        ImGui.AddRectFilled(drawList, trackMin, trackMax, trackCol, trackRounding);

        // Sliding knob: travel from left edge (+knobRadius+2) to right edge (-knobRadius-2)
        var travel = trackW - (knobRadius + 2f) * 2f;
        var knobX = cursor.X + knobRadius + 2f + travel * t;
        var knobY = cursor.Y + trackH * 0.5f;

        ImGui.AddCircleFilled(drawList, new Vector2(knobX, knobY), knobRadius, knobCol);

        return changed;
    }

    /// <summary>
    ///     Draws a subtle accent glow rect behind the previous slider when it is hovered or active.
    /// </summary>
    public static void SliderGlow()
    {
        if (!ImGui.IsItemHovered() && !ImGui.IsItemActive())
            return;

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var glowColor = Theme.AccentPrimary with { W = 0.12f };
        var col = ImGui.ColorConvertFloat4ToU32(glowColor);
        var drawList = ImGui.GetWindowDrawList();

        // Draw glow behind the slider (use same rounding as FrameRounding)
        ImGui.AddRectFilled(drawList, min, max, col, ImGui.GetStyle().FrameRounding);
    }

    /// <summary>
    ///     Renders an accent-colored button for primary actions.
    /// </summary>
    public static bool PrimaryButton(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.AccentPrimary with { W = 0.7f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentPrimary with { W = 0.85f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentPrimary);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Background);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(4);
        return clicked;
    }

    /// <summary>
    ///     Renders an error-colored button for destructive actions.
    /// </summary>
    public static bool DangerButton(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Error with { W = 0.6f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Error with { W = 0.8f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Error);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(3);
        return clicked;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static Vector4 LerpColor(Vector4 a, Vector4 b, float t) =>
        new(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t,
            a.W + (b.W - a.W) * t
        );
}
