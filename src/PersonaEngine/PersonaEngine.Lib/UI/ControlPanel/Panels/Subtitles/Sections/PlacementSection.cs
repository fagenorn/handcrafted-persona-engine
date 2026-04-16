using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Subtitles.Sections;

/// <summary>
///     Where subtitles sit on the canvas, how many lines stay visible, and how
///     snappy the reveal feels. Every knob uses a perceptually-anchored
///     <see cref="ImGuiHelpers.LabeledSlider" /> instead of a raw slider.
/// </summary>
public sealed class PlacementSection : IDisposable
{
    private readonly IConfigWriter _configWriter;
    private readonly IDisposable? _changeSubscription;

    private SubtitleOptions _opts;

    public PlacementSection(IOptionsMonitor<SubtitleOptions> monitor, IConfigWriter configWriter)
    {
        _configWriter = configWriter;
        _opts = monitor.CurrentValue;
        _changeSubscription = monitor.OnChange((updated, _) => _opts = updated);
    }

    public void Dispose() => _changeSubscription?.Dispose();

    public void Render(float dt)
    {
        using (Ui.Card("##placement", padding: 12f))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
            ImGui.TextUnformatted("Placement & Motion");
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted(
                "Where subtitles sit on the canvas, and how snappy the reveal feels."
            );
            ImGui.PopStyleColor();
            ImGui.Spacing();

            RenderMaxLinesRow(dt);
            RenderBottomMarginRow(dt);
            RenderSideMarginRow(dt);
            RenderInterSegmentRow(dt);
            RenderAnimationRow(dt);
        }
    }

    private void RenderMaxLinesRow(float dt)
    {
        var rowY = ImGui.GetCursorPosY();

        ImGuiHelpers.SettingLabel(
            "Max Lines",
            "Maximum captions visible at once. Older lines scroll off as new ones appear."
        );

        var lines = _opts.MaxVisibleLines;
        if (
            ImGuiHelpers.LabeledSlider("##SubtitleMaxLines", ref lines, 1, 10, "Single", "Many", dt)
        )
        {
            _opts = _opts with { MaxVisibleLines = lines };
            _configWriter.Write(_opts);
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }

    private void RenderBottomMarginRow(float dt)
    {
        var rowY = ImGui.GetCursorPosY();

        ImGuiHelpers.SettingLabel(
            "From Bottom",
            "Distance from the bottom edge of the canvas, in pixels."
        );

        var margin = _opts.BottomMargin;
        if (
            ImGuiHelpers.LabeledSlider(
                "##SubtitleBottomMargin",
                ref margin,
                0,
                500,
                "Flush",
                "High",
                dt
            )
        )
        {
            _opts = _opts with { BottomMargin = margin };
            _configWriter.Write(_opts);
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }

    private void RenderSideMarginRow(float dt)
    {
        var rowY = ImGui.GetCursorPosY();

        ImGuiHelpers.SettingLabel(
            "Side Padding",
            "Horizontal padding on both edges of the text column, in pixels."
        );

        var margin = _opts.SideMargin;
        if (
            ImGuiHelpers.LabeledSlider(
                "##SubtitleSideMargin",
                ref margin,
                0,
                400,
                "Tight",
                "Wide",
                dt
            )
        )
        {
            _opts = _opts with { SideMargin = margin };
            _configWriter.Write(_opts);
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }

    private void RenderInterSegmentRow(float dt)
    {
        var rowY = ImGui.GetCursorPosY();

        ImGuiHelpers.SettingLabel(
            "Line Spacing",
            "Vertical gap between stacked caption segments, in pixels."
        );

        var spacing = _opts.InterSegmentSpacing;
        if (
            ImGuiHelpers.LabeledSlider(
                "##SubtitleInterSeg",
                ref spacing,
                0f,
                80f,
                "Compact",
                "Spacious",
                "%.0f",
                dt
            )
        )
        {
            _opts = _opts with { InterSegmentSpacing = spacing };
            _configWriter.Write(_opts);
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }

    private void RenderAnimationRow(float dt)
    {
        var rowY = ImGui.GetCursorPosY();

        ImGuiHelpers.SettingLabel(
            "Reveal Motion",
            "How quickly each new caption fades and slides in."
        );

        var duration = _opts.AnimationDuration;
        if (
            ImGuiHelpers.LabeledSlider(
                "##SubtitleAnim",
                ref duration,
                0.05f,
                1.0f,
                "Snappy",
                "Smooth",
                "%.2f",
                dt
            )
        )
        {
            _opts = _opts with { AnimationDuration = duration };
            _configWriter.Write(_opts);
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }
}
