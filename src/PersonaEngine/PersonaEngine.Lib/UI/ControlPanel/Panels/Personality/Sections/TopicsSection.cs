using System.Numerics;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Personality.Sections;

/// <summary>
///     Chip-based topic input. Each topic renders as a removable chip.
///     A text input below the chips allows adding new topics.
///     Maps to <see cref="ConversationContextOptions.Topics" />.
/// </summary>
public sealed class TopicsSection : IDisposable
{
    private const int InputBufferSize = 256;
    private const float ChipGap = 6f;
    private const float ChipPaddingX = 10f;
    private const float ChipPaddingY = 4f;
    private const float ChipRounding = 12f;
    private const float ChipCloseSize = 14f;
    private const float ChipCloseGap = 6f;

    private readonly IConfigWriter _configWriter;
    private readonly IDisposable? _changeSubscription;

    private ConversationContextOptions _opts;
    private string _inputBuffer = string.Empty;

    public TopicsSection(
        IOptionsMonitor<ConversationContextOptions> monitor,
        IConfigWriter configWriter
    )
    {
        _configWriter = configWriter;
        _opts = monitor.CurrentValue;
        _changeSubscription = monitor.OnChange((updated, _) => _opts = updated);
    }

    public void Dispose() => _changeSubscription?.Dispose();

    public void Render(float dt)
    {
        using (Ui.Card("##topics", padding: 12f))
        {
            // Header
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
            ImGui.TextUnformatted("Topics");
            ImGui.PopStyleColor();

            // Description
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted("What should the conversation focus on?");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            // Chips for existing topics
            RenderTopicChips();

            // Input for adding new topics
            RenderAddInput();
        }
    }

    private void RenderTopicChips()
    {
        var topics = _opts.Topics;
        if (topics.Count == 0)
            return;

        int? removeIndex = null;
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var currentLineWidth = 0f;
        var isFirstOnLine = true;

        for (var i = 0; i < topics.Count; i++)
        {
            var topic = topics[i];
            var chipWidth = MeasureRemovableChipWidth(topic);

            // Wrap to next line if needed
            if (!isFirstOnLine && currentLineWidth + ChipGap + chipWidth > availableWidth)
            {
                currentLineWidth = 0f;
                isFirstOnLine = true;
            }

            if (!isFirstOnLine)
            {
                ImGui.SameLine(0f, ChipGap);
                currentLineWidth += ChipGap;
            }

            if (RenderRemovableChip(topic, i))
            {
                removeIndex = i;
            }

            currentLineWidth += chipWidth;
            isFirstOnLine = false;
        }

        if (removeIndex.HasValue)
        {
            var updated = new List<string>(_opts.Topics);
            updated.RemoveAt(removeIndex.Value);
            _opts = _opts with { Topics = updated };
            _configWriter.Write(_opts);
        }

        ImGui.Spacing();
    }

    private void RenderAddInput()
    {
        var buttonWidth = ImGui.CalcTextSize("Add").X + ImGui.GetStyle().FramePadding.X * 2f;
        var gap = 6f;

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - buttonWidth - gap);
        ImGui.InputTextWithHint(
            "##add_topic",
            "Add a topic...",
            ref _inputBuffer,
            InputBufferSize
        );
        var enterPressed =
            ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.Enter);

        ImGui.SameLine(0f, gap);
        var hasInput = _inputBuffer.Trim().Length > 0;
        if (!hasInput)
            ImGui.BeginDisabled();
        var buttonPressed = ImGui.Button("Add##add_topic_btn");
        if (!hasInput)
            ImGui.EndDisabled();

        if (enterPressed || buttonPressed)
        {
            var trimmed = _inputBuffer.Trim();
            if (
                trimmed.Length > 0
                && !_opts.Topics.Contains(trimmed, StringComparer.OrdinalIgnoreCase)
            )
            {
                var updated = new List<string>(_opts.Topics) { trimmed };
                _opts = _opts with { Topics = updated };
                _configWriter.Write(_opts);
            }

            _inputBuffer = string.Empty;
        }
    }

    /// <summary>
    ///     Renders a chip with an integrated close (x) icon. Returns true if the user
    ///     clicked the close area.
    /// </summary>
    private static bool RenderRemovableChip(string label, int index)
    {
        var textSize = ImGui.CalcTextSize(label);
        var totalWidth = ChipPaddingX + textSize.X + ChipCloseGap + ChipCloseSize + ChipPaddingX;
        var totalHeight = textSize.Y + ChipPaddingY * 2f;
        var size = new Vector2(totalWidth, totalHeight);

        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##topic_{index}", size);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var min = cursor;
        var max = cursor + size;

        // Background
        Vector4 fill = hovered ? Theme.SurfaceHover : Theme.Surface2;
        ImGui.AddRectFilled(drawList, min, max, ImGui.ColorConvertFloat4ToU32(fill), ChipRounding);

        // Border
        ImGui.AddRect(
            drawList,
            min,
            max,
            ImGui.ColorConvertFloat4ToU32(Theme.AccentPrimary with { W = 0.3f }),
            ChipRounding,
            0,
            1f
        );

        // Label text
        var textPos = new Vector2(min.X + ChipPaddingX, min.Y + ChipPaddingY);
        drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(Theme.TextPrimary), label);

        // Close icon (x)
        var closeCenter = new Vector2(
            max.X - ChipPaddingX - ChipCloseSize * 0.5f,
            min.Y + totalHeight * 0.5f
        );
        var crossHalf = 4f;
        var closeColor = ImGui.ColorConvertFloat4ToU32(
            hovered ? Theme.TextPrimary : Theme.TextSecondary
        );
        drawList.AddLine(
            closeCenter - new Vector2(crossHalf, crossHalf),
            closeCenter + new Vector2(crossHalf, crossHalf),
            closeColor,
            1.5f
        );
        drawList.AddLine(
            closeCenter + new Vector2(-crossHalf, crossHalf),
            closeCenter + new Vector2(crossHalf, -crossHalf),
            closeColor,
            1.5f
        );

        return clicked;
    }

    private static float MeasureRemovableChipWidth(string label)
    {
        var textWidth = ImGui.CalcTextSize(label).X;
        return ChipPaddingX + textWidth + ChipCloseGap + ChipCloseSize + ChipPaddingX;
    }
}
