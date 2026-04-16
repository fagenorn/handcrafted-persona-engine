using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
///     Shared zero-allocation rendering helpers for the control panel.
/// </summary>
public static class ImGuiHelpers
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

    /// <summary>
    ///     Renders a collapsing header styled like <see cref="SectionHeader" /> (accent-colored
    ///     title, soft gradient divider). Content inside <paramref name="renderContent" /> is
    ///     only drawn when expanded.
    /// </summary>
    /// <param name="label">Header label.</param>
    /// <param name="subtitle">Optional muted one-liner beneath the header, always visible.</param>
    /// <param name="defaultOpen">Whether the section starts expanded on first render.</param>
    /// <param name="renderContent">Content callback, invoked when expanded.</param>
    /// <summary>
    ///     Renders a collapsing header styled with <see cref="Theme.AccentPrimary" /> for the
    ///     label (and arrow). An optional <paramref name="hint" /> is drawn inline on the header
    ///     bar in <see cref="Theme.TextTertiary" /> at a smaller font size, baseline-aligned with
    ///     the label. An optional <paramref name="subtitle" /> renders as a separate line below.
    /// </summary>
    /// <summary>
    ///     Persistent state for an animated collapsible section. Each section that uses
    ///     <see cref="CollapsibleSection" /> must hold one of these as a field.
    /// </summary>
    public sealed class CollapsibleState
    {
        internal bool IsOpen;
        internal bool Initialized;
        internal AnimatedFloat HeightAnim = new(0f);
        internal float ContentHeight;
    }

    /// <summary>
    ///     Renders a collapsing header with animated expand/collapse, styled with
    ///     <see cref="Theme.AccentPrimary" /> for the label and arrow. Optional inline
    ///     <paramref name="hint" /> on the header bar. Content height smoothly interpolates
    ///     via <see cref="CollapsibleState" />.
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
        // Toggle logic — use ImGui's CollapsingHeader for the click, but drive
        // the visual state ourselves so we can animate the content height.
        ImGui.SetNextItemOpen(defaultOpen, ImGuiCond.FirstUseEver);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.AccentPrimary);
        var headerOpen = ImGui.CollapsingHeader($"##{label}_header");
        ImGui.PopStyleColor();
        HandCursorOnHover();

        // Draw label + hint on the header bar
        RenderCollapsibleHeaderText(label, hint);

        if (subtitle is not null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted(subtitle);
            ImGui.PopStyleColor();
        }

        // If no animation state provided, snap open/close (backwards compat)
        if (animState is null)
        {
            if (headerOpen)
                renderContent();
            return;
        }

        // Initialize on first frame
        if (!animState.Initialized)
        {
            animState.IsOpen = defaultOpen;
            animState.HeightAnim = new AnimatedFloat(defaultOpen ? 1f : 0f);
            animState.Initialized = true;
        }

        // Detect toggle
        if (headerOpen != animState.IsOpen)
            animState.IsOpen = headerOpen;

        animState.HeightAnim.Target = animState.IsOpen ? 1f : 0f;
        animState.HeightAnim.Update(dt);

        var t = Math.Clamp(animState.HeightAnim.Current, 0f, 1f);

        // Always render content so we can measure it, but clip to animated height
        if (t > 0.001f || animState.IsOpen)
        {
            var clipHeight = animState.ContentHeight * t;
            var cursorStart = ImGui.GetCursorScreenPos();

            // Clip the content region
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

            // Measure actual content height for next frame's animation target
            animState.ContentHeight = contentEndY - contentStartY;

            // Advance cursor by the clipped height (not the full content height)
            // so the layout below slides smoothly. Dummy is required after
            // SetCursorPosY to register the new bounds with ImGui.
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
    /// <summary>
    ///     Standard row height for a setting (label + widget). Widgets shorter than this
    ///     (e.g., ToggleSwitch at 20px) get padded so all settings have consistent spacing.
    ///     Computed from ImGui's standard framed-widget height (FramePadding.Y * 2 + FontSize).
    /// </summary>
    public static float SettingRowHeight => ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y;

    public static void SettingLabel(string label, string? tooltip, float? labelWidth = null)
    {
        var width = labelWidth ?? Math.Clamp(ImGui.GetContentRegionAvail().X * 0.30f, 130f, 240f);

        // Align plain text vertically to the center of framed widgets (Combo, Slider, etc.)
        // so label and widget share the same visual midline.
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

    /// <summary>
    ///     Call after each setting widget (toggle, combo, slider, etc.) to ensure the row
    ///     occupies at least <see cref="SettingRowHeight" /> pixels. This makes all settings
    ///     evenly spaced regardless of widget type.
    /// </summary>
    public static void SettingEndRow(float rowStartY)
    {
        var elapsed = ImGui.GetCursorPosY() - rowStartY;
        var minHeight = SettingRowHeight;
        if (elapsed < minHeight)
            ImGui.Dummy(new Vector2(0f, minHeight - elapsed));
    }

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

    /// <summary>
    ///     Renders an accent-colored button for primary actions.
    /// </summary>
    public static bool PrimaryButton(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.AccentPrimary with { W = 0.7f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentPrimary with { W = 0.85f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentPrimary);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Base);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(4);
        HandCursorOnHover();
        return clicked;
    }

    /// <summary>
    ///     Pill-shaped toggle rendered with <see cref="Theme.AccentPrimary" /> tint when selected
    ///     and <see cref="Theme.Surface2" /> when not. Used for filter/tag selectors.
    /// </summary>
    /// <returns><see langword="true"/> if clicked this frame.</returns>
    public static bool Chip(string label, bool selected, bool interactive = true)
    {
        var padding = new Vector2(10f, 4f);
        var rounding = 12f;

        var textSize = ImGui.CalcTextSize(label);
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
        ImGui.GetWindowDrawList().AddText(textPos, ImGui.ColorConvertFloat4ToU32(textColor), label);

        return clicked;
    }

    /// <summary>
    ///     Renders a primary button with press feedback (brief darkening)
    ///     and a warm glow pulse on click.
    /// </summary>
    public static bool PrimaryButtonWithFeedback(
        string label,
        ref OneShotAnimation pressAnim,
        float dt
    )
    {
        pressAnim.Update(dt);

        // Darken slightly on press (non-linear pulse)
        var baseAlpha = 0.7f;
        if (pressAnim.IsActive)
        {
            var pressT = pressAnim.Progress;
            var darken = (pressT < 0.3f ? pressT / 0.3f : (1f - pressT) / 0.7f) * 0.05f;
            baseAlpha = 0.7f - darken;
        }

        ImGui.PushStyleColor(ImGuiCol.Button, Theme.AccentPrimary with { W = baseAlpha });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentPrimary with { W = 0.85f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentPrimary);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Base);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(4);
        HandCursorOnHover();

        if (clicked)
        {
            pressAnim.Start(0.15f);

            // Warm glow pulse around the button on click
            var drawList = ImGui.GetWindowDrawList();
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var glowCol = ImGui.ColorConvertFloat4ToU32(Theme.AccentPrimary with { W = 0.15f });
            var padding = new Vector2(4f, 4f);
            ImGui.AddRectFilled(
                drawList,
                min - padding,
                max + padding,
                glowCol,
                ImGui.GetStyle().FrameRounding + 2f
            );
        }

        return clicked;
    }

    /// <summary>
    ///     Renders a low-emphasis button for secondary actions like "Reset defaults".
    ///     When <paramref name="enabled" /> is <see langword="false" /> the button is
    ///     dimmed and non-interactive; when <see langword="true" /> it uses secondary
    ///     text color with surface-hover feedback so it's clearly clickable.
    /// </summary>
    public static bool SubtleButton(string label, bool enabled = true)
    {
        if (!enabled)
            ImGui.BeginDisabled();

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f, 5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Surface3);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.SurfaceHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentPrimary with { W = 0.4f });
        ImGui.PushStyleColor(ImGuiCol.Text, enabled ? Theme.TextSecondary : Theme.TextTertiary);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(2);

        if (!enabled)
            ImGui.EndDisabled();
        else
            HandCursorOnHover();

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
        HandCursorOnHover();
        return clicked;
    }

    // ── Preview Button ─────────────────────────────────────────────────────────

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

    /// <summary>
    ///     Compact square icon button (e.g., "A/B"). Accent-tinted to match
    ///     <see cref="PrimaryButton" />. Uses <see cref="ImGui.Button(string, Vector2)" /> for
    ///     reliable text centering — ImGui auto-strips <c>##id</c> suffixes for display.
    ///     Tooltip rendered via <see cref="Tooltip" /> when hovered.
    /// </summary>
    /// <returns><see langword="true"/> if clicked this frame.</returns>
    public static bool IconButton(string glyph, float size = 28f, string? tooltip = null)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.AccentPrimary with { W = 0.6f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentPrimary with { W = 0.85f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentPrimary);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Base);
        var clicked = ImGui.Button(glyph, new Vector2(size, size));
        ImGui.PopStyleColor(4);
        HandCursorOnHover();

        if (tooltip is not null)
            Tooltip(tooltip);

        return clicked;
    }

    // ── Resolution Picker ────────────────────────────────────────────────────────

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

    // ── Helpers ──────────────────────────────────────────────────────────────────

    public static Vector4 LerpColor(Vector4 a, Vector4 b, float t) =>
        new(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t,
            a.W + (b.W - a.W) * t
        );
}
