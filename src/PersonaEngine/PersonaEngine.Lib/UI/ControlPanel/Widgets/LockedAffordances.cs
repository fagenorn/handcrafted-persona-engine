using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
///     Inert affordances used in place of clickable controls when a feature isn't
///     installed in the current profile. Click is a true no-op (not <c>BeginDisabled</c>)
///     so we never accidentally persist a setting the runtime can't honour. The lock
///     glyph + tooltip explain how to unlock — typically "re-run the installer with the
///     <em>Build with it</em> profile."
/// </summary>
public static partial class ImGuiHelpers
{
    /// <summary>
    ///     Standard tooltip text for any locked affordance. Always tells the user
    ///     which profile installs the missing assets so they can self-serve.
    /// </summary>
    public static string LockedTooltip(string featureName, string requiredProfile) =>
        $"{featureName} is locked.\nRe-run the installer and pick \"{requiredProfile}\" to download the assets.";

    /// <summary>
    ///     Draws a small padlock at <paramref name="position" /> sized to fit a
    ///     <paramref name="size" />×<paramref name="size" /> square. Body is filled,
    ///     shackle is stroked — matches the play-triangle / stop-square idiom in
    ///     <see cref="PreviewButton" />. Doesn't advance the cursor.
    /// </summary>
    public static void DrawLockIcon(
        ImDrawListPtr drawList,
        Vector2 position,
        float size,
        Vector4 color
    )
    {
        var col = ImGui.ColorConvertFloat4ToU32(color);

        // Body: bottom ~55% of the icon, slight rounding.
        var bodyW = size * 0.78f;
        var bodyH = size * 0.55f;
        var bodyX = position.X + (size - bodyW) * 0.5f;
        var bodyY = position.Y + size - bodyH;
        ImGui.AddRectFilled(
            drawList,
            new Vector2(bodyX, bodyY),
            new Vector2(bodyX + bodyW, bodyY + bodyH),
            col,
            size * 0.12f
        );

        // Shackle: stroked U starting from inside the body's top, arcing over.
        // Drawn as straight legs + half-circle so it reads well at small sizes.
        var stroke = MathF.Max(1f, size * 0.11f);
        var shackleR = bodyW * 0.32f;
        var shackleCx = position.X + size * 0.5f;
        var shackleTopY = position.Y + stroke * 0.5f + shackleR * 0.05f;
        var shackleArcCy = shackleTopY + shackleR;
        var legX1 = shackleCx - shackleR;
        var legX2 = shackleCx + shackleR;

        // Left leg up
        ImGui.PathLineTo(drawList, new Vector2(legX1, bodyY));
        // Arc PI → 2*PI: left → up → right (top half, clockwise)
        ImGui.PathArcTo(
            drawList,
            new Vector2(shackleCx, shackleArcCy),
            shackleR,
            MathF.PI,
            2f * MathF.PI,
            16
        );
        // Right leg back down to the body
        ImGui.PathLineTo(drawList, new Vector2(legX2, bodyY));
        ImGui.PathStroke(drawList, col, ImDrawFlags.None, stroke);

        // Keyhole: tiny dot in the body so the icon reads as a lock at a glance.
        var keyholeR = size * 0.07f;
        ImGui.AddCircleFilled(
            drawList,
            new Vector2(shackleCx, bodyY + bodyH * 0.5f),
            keyholeR,
            ImGui.ColorConvertFloat4ToU32(Theme.Surface1 with { W = color.W }),
            8
        );
    }

    /// <summary>
    ///     Reserves an inline icon-sized box and draws the lock glyph into it.
    ///     Returns the icon's screen-space size so callers can SameLine the label.
    /// </summary>
    private static float RenderInlineLock(float size, Vector4 color)
    {
        var pos = ImGui.GetCursorScreenPos();
        // Vertically center against a single text line.
        var lineH = ImGui.GetTextLineHeight();
        var iconY = pos.Y + MathF.Max(0f, (lineH - size) * 0.5f);
        DrawLockIcon(ImGui.GetWindowDrawList(), new Vector2(pos.X, iconY), size, color);
        ImGui.Dummy(new Vector2(size, lineH));
        return size;
    }

    /// <summary>
    ///     Pill rendered in the dim "locked" treatment: muted fill, tertiary text,
    ///     vector lock prefix. Click never fires a callback. Hover shows the supplied
    ///     tooltip and the cursor stays a default arrow.
    /// </summary>
    public static void LockedChip(string label, string featureName, string requiredProfile)
    {
        var padding = new Vector2(10f, 4f);
        var rounding = 12f;

        var hashIdx = label.IndexOf("##", StringComparison.Ordinal);
        var displayLabel = hashIdx >= 0 ? label[..hashIdx] : label;

        var iconSize = ImGui.GetTextLineHeight() * 0.9f;
        var iconGap = 6f;
        var textSize = ImGui.CalcTextSize(displayLabel);
        var contentW = iconSize + iconGap + textSize.X;
        var contentH = MathF.Max(iconSize, textSize.Y);
        var size = new Vector2(contentW + padding.X * 2f, contentH + padding.Y * 2f);
        var cursor = ImGui.GetCursorScreenPos();

        // Reserve space + claim hover region without producing a click.
        ImGui.Dummy(size);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var min = cursor;
        var max = cursor + size;
        var fill = Theme.Surface2 with { W = 0.5f };
        var textColor = Theme.TextTertiary;

        ImGui.AddRectFilled(drawList, min, max, ImGui.ColorConvertFloat4ToU32(fill), rounding);

        // Lock + label, both vertically centered against contentH.
        var iconY = min.Y + padding.Y + (contentH - iconSize) * 0.5f;
        DrawLockIcon(drawList, new Vector2(min.X + padding.X, iconY), iconSize, textColor);

        var textPos = new Vector2(
            min.X + padding.X + iconSize + iconGap,
            min.Y + padding.Y + (contentH - textSize.Y) * 0.5f
        );
        drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(textColor), displayLabel);

        if (hovered)
        {
            ImGui.SetTooltip(LockedTooltip(featureName, requiredProfile));
        }
    }

    /// <summary>
    ///     Card-shaped locked affordance suitable for the big mode-selector cards
    ///     (Voice → Expressive, etc.). Visually parallels <see cref="Ui.Card" /> so
    ///     the layout doesn't shift when a feature is locked, but is non-clickable
    ///     and dimmed.
    ///     <para>
    ///         Body intentionally mirrors the unlocked card: title + wrapped subtitle
    ///         only. The unlock hint lives in the tooltip — putting it in the body
    ///         would make this card taller than its sibling and shift the layout.
    ///     </para>
    /// </summary>
    public static void LockedCard(
        string id,
        string title,
        string subtitle,
        string featureName,
        string requiredProfile,
        float width,
        float padding = 25f
    )
    {
        using (Ui.Card(id, padding: padding, width: width))
        {
            // Title row: vector lock glyph + dimmed title text.
            var titleColor = Theme.TextTertiary;
            var iconSize = ImGui.GetTextLineHeight() * 0.95f;
            RenderInlineLock(iconSize, titleColor);
            ImGui.SameLine(0f, 6f);
            ImGui.PushStyleColor(ImGuiCol.Text, titleColor);
            ImGui.TextUnformatted(title);
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary with { W = 0.85f });
            ImGui.PushTextWrapPos(0f);
            ImGui.TextUnformatted(subtitle);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
        }

        // Card uses BeginChild internally — IsItemHovered after EndChild reports the
        // child window's hover, which is what we want for tooltips.
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(LockedTooltip(featureName, requiredProfile));
        }
    }

    /// <summary>
    ///     Empty-state body for sections whose contents are unavailable in the
    ///     current profile (e.g. Voice gallery in Expressive mode without Qwen3).
    ///     Centered single line with a vector lock icon and the upgrade hint.
    /// </summary>
    public static void LockedSection(string featureName, string requiredProfile)
    {
        var message = $"{featureName} requires the \"{requiredProfile}\" profile.";

        var iconSize = ImGui.GetTextLineHeight() * 0.95f;
        var iconGap = 8f;
        var textWidth = ImGui.CalcTextSize(message).X;
        var totalWidth = iconSize + iconGap + textWidth;

        var avail = ImGui.GetContentRegionAvail().X;
        var indent = MathF.Max(0f, (avail - totalWidth) * 0.5f);

        ImGui.Dummy(new Vector2(0f, 6f));
        ImGui.Indent(indent);
        RenderInlineLock(iconSize, Theme.TextTertiary);
        ImGui.SameLine(0f, iconGap);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
        ImGui.TextUnformatted(message);
        ImGui.PopStyleColor();
        ImGui.Unindent(indent);
        ImGui.Dummy(new Vector2(0f, 6f));
    }
}
