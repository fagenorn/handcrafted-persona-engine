using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Models;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Sections;

/// <summary>
///     Stateless tile renderer for a <see cref="VoiceDescriptor" />. The caller decides
///     whether the tile is selected and how to handle click results. Reused by the gallery
///     and the clone picker.
/// </summary>
public static class VoiceTile
{
    /// <summary>Minimum tile width in the horizontal scroll strip.</summary>
    private const float MinWidth = 200f;

    /// <summary>Padding inside each tile card.</summary>
    private const float Padding = 10f;

    public static VoiceTileResult Render(
        VoiceDescriptor descriptor,
        bool selected,
        ImGuiHelpers.PreviewButtonState previewState,
        float elapsed,
        float uniformHeight = 0f
    )
    {
        var result = default(VoiceTileResult);
        var id = $"##voice_tile_{descriptor.Engine}_{descriptor.Id}";

        // Always use a fixed height: either the tracked uniform height, or a generous
        // default on the first frame. AutoResizeY does not work inside SameLine contexts.
        var cardHeight = uniformHeight > 0f ? uniformHeight : 200f;

        using (Ui.Card(id, padding: Padding, border: true, width: MinWidth, height: cardHeight))
        {
            if (selected)
            {
                var drawList = ImGui.GetWindowDrawList();
                var min = ImGui.GetWindowPos();
                var max = min + ImGui.GetWindowSize();
                ImGui.AddRectFilled(
                    drawList,
                    min,
                    max,
                    ImGui.ColorConvertFloat4ToU32(Theme.AccentPrimary with { W = 0.12f })
                );
            }

            // Track the cursor Y so we can measure actual content height.
            var contentStartY = ImGui.GetCursorPosY();

            // Row 1: display name + preview button
            ImGui.PushStyleColor(ImGuiCol.Text, selected ? Theme.AccentPrimary : Theme.TextPrimary);
            ImGui.TextUnformatted(descriptor.DisplayName);
            ImGui.PopStyleColor();

            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 10f);
            if (
                ImGuiHelpers.PreviewButton(
                    $"##tile_preview_{descriptor.Engine}_{descriptor.Id}",
                    previewState,
                    elapsed,
                    ImGuiHelpers.PreviewButtonSize.Compact
                )
            )
            {
                result.PreviewClicked = true;
            }

            // Row 2: gender (muted)
            if (descriptor.Gender is { } gender)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
                ImGui.TextUnformatted(gender.ToString());
                ImGui.PopStyleColor();
            }

            // Row 3: description (muted, wrapped)
            if (descriptor.Description is { } description)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
                ImGui.PushTextWrapPos(0f);
                ImGui.TextUnformatted(description);
                ImGui.PopTextWrapPos();
                ImGui.PopStyleColor();
            }

            // Measure actual content height (cursor delta + top/bottom padding).
            // This is reliable regardless of AutoResizeY or SameLine context.
            result.ContentHeight = ImGui.GetCursorPosY() - contentStartY + Padding * 2f;

            // Tile-body click: any click on empty space selects the voice.
            if (
                ImGui.IsWindowHovered(ImGuiHoveredFlags.None)
                && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                && !ImGui.IsAnyItemHovered()
            )
            {
                result.SelectClicked = true;
            }
        }

        return result;
    }
}

/// <summary>
///     Click results from a tile render. Multiple flags may fire in the same frame (rare).
/// </summary>
public struct VoiceTileResult
{
    public bool PreviewClicked;
    public bool SelectClicked;

    /// <summary>
    ///     Actual content height of this tile (cursor delta + padding), measured inside the
    ///     card. Fed to <see cref="UniformHeightTracker" /> so the tallest tile's height
    ///     is enforced on all tiles next frame.
    /// </summary>
    public float ContentHeight;
}
