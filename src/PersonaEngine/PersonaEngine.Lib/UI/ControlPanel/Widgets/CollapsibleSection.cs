using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

public static partial class ImGuiHelpers
{
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
}
