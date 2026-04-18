using System.Numerics;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Listening.Sections;

/// <summary>
///     Recognition card: Whisper decoder preset (Fast/Balanced/Accurate) + a chip-based
///     Custom Vocabulary editor backed by <see cref="AsrConfiguration.TtsPrompt" />.
/// </summary>
public sealed class RecognitionSection : IDisposable
{
    private const int InputBufferSize = 128;
    private const int MaxVocabulary = 30;
    private const float ChipGap = 6f;
    private const float ChipPaddingX = 10f;
    private const float ChipPaddingY = 4f;
    private const float ChipRounding = 12f;
    private const float ChipCloseSize = 14f;
    private const float ChipCloseGap = 6f;

    private static readonly (string Label, WhisperConfigTemplate Value)[] QualityOptions =
    {
        ("Fast", WhisperConfigTemplate.Performant),
        ("Balanced", WhisperConfigTemplate.Balanced),
        ("Accurate", WhisperConfigTemplate.Precise),
    };

    private readonly IConfigWriter _configWriter;
    private readonly IDisposable? _changeSubscription;

    // Pre-built "##vocab_{i}" ids up to MaxVocabulary so the per-chip
    // InvisibleButton doesn't interpolate a fresh string every frame.
    private static readonly string[] VocabChipIds = BuildVocabChipIds();

    private AsrConfiguration _asr;
    private string _inputBuffer = string.Empty;
    private List<string> _vocabulary = new();

    private static string[] BuildVocabChipIds()
    {
        var ids = new string[MaxVocabulary];
        for (var i = 0; i < MaxVocabulary; i++)
        {
            ids[i] = $"##vocab_{i}";
        }
        return ids;
    }

    public RecognitionSection(IOptionsMonitor<AsrConfiguration> monitor, IConfigWriter configWriter)
    {
        _configWriter = configWriter;
        _asr = monitor.CurrentValue;
        _vocabulary = ParseVocabulary(_asr.TtsPrompt);
        _changeSubscription = monitor.OnChange(
            (updated, _) =>
            {
                _asr = updated;
                _vocabulary = ParseVocabulary(updated.TtsPrompt);
            }
        );
    }

    public void Dispose() => _changeSubscription?.Dispose();

    public void Render(float dt)
    {
        using (Ui.Card("##recognition", padding: 12f))
        {
            RenderHeader();
            RenderQuality();
            ImGui.Spacing();
            RenderVocabulary();
        }
    }

    private static void RenderHeader()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
        ImGui.TextUnformatted("Recognition");
        ImGui.PopStyleColor();

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted("How we turn your speech into text for the avatar");
        ImGui.PopStyleColor();

        ImGui.Spacing();
    }

    private void RenderQuality()
    {
        ImGuiHelpers.SettingLabel(
            "Quality",
            "Trade-off between speed and accuracy of transcription."
        );

        for (var i = 0; i < QualityOptions.Length; i++)
        {
            var (label, value) = QualityOptions[i];
            var selected = _asr.TtsMode == value;
            if (ImGuiHelpers.Chip(label, selected))
            {
                _asr = _asr with { TtsMode = value };
                _configWriter.Write(_asr);
            }

            if (i < QualityOptions.Length - 1)
                ImGui.SameLine(0f, ChipGap);
        }
    }

    private void RenderVocabulary()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
        ImGui.TextUnformatted("Custom Vocabulary");
        ImGui.PopStyleColor();

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted(
            "Words we should recognize correctly — your avatar's name, show name, recurring topics."
        );
        ImGui.PopStyleColor();

        ImGui.Spacing();

        RenderChips();
        RenderAddInput();
    }

    private void RenderChips()
    {
        if (_vocabulary.Count == 0)
            return;

        int? removeIndex = null;
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var currentLineWidth = 0f;
        var isFirstOnLine = true;

        for (var i = 0; i < _vocabulary.Count; i++)
        {
            var label = _vocabulary[i];
            var chipWidth = MeasureRemovableChipWidth(label);

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

            if (RenderRemovableChip(label, i))
            {
                removeIndex = i;
            }

            currentLineWidth += chipWidth;
            isFirstOnLine = false;
        }

        if (removeIndex.HasValue)
        {
            _vocabulary.RemoveAt(removeIndex.Value);
            CommitVocabulary();
        }

        ImGui.Spacing();
    }

    private void RenderAddInput()
    {
        var buttonWidth = ImGui.CalcTextSize("Add").X + ImGui.GetStyle().FramePadding.X * 2f;
        var gap = 6f;

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - buttonWidth - gap);
        ImGui.InputTextWithHint("##addvoc", "Add a word...", ref _inputBuffer, InputBufferSize);
        var enterPressed = ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.Enter);

        ImGui.SameLine(0f, gap);
        var hasInput = _inputBuffer.Trim().Length > 0;
        if (!hasInput)
            ImGui.BeginDisabled();
        var buttonPressed = ImGui.Button("Add##add_vocab_btn");
        ImGuiHelpers.HandCursorOnHover();
        if (!hasInput)
            ImGui.EndDisabled();

        if ((enterPressed || buttonPressed) && _vocabulary.Count < MaxVocabulary)
        {
            var trimmed = _inputBuffer.Trim();
            if (
                trimmed.Length > 0
                && !_vocabulary.Contains(trimmed, StringComparer.OrdinalIgnoreCase)
            )
            {
                _vocabulary.Add(trimmed);
                CommitVocabulary();
            }

            _inputBuffer = string.Empty;
        }
    }

    private void CommitVocabulary()
    {
        _asr = _asr with { TtsPrompt = string.Join(", ", _vocabulary) };
        _configWriter.Write(_asr);
    }

    private static List<string> ParseVocabulary(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return new List<string>();

        var parts = prompt.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        return new List<string>(parts);
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
        // Chip limit is enforced at the add-input gate; defensively clamp here
        // so a future refactor that lifts MaxVocabulary can't index past the
        // cached id table.
        var chipId = index < VocabChipIds.Length ? VocabChipIds[index] : $"##vocab_{index}";
        var clicked = ImGui.InvisibleButton(chipId, size);
        ImGuiHelpers.HandCursorOnHover();
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
