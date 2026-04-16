using System.Numerics;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Subtitles.Sections;

/// <summary>
///     Live styled preview of the subtitle rendering. Read-only — shows text color,
///     highlight color, and outline thickness in proportion. The preview uses the
///     default ImGui font (not the performer's TTF) and runs at a fixed preview
///     size, so font-face fidelity and exact output size are not represented.
/// </summary>
public sealed class PreviewSection : IDisposable
{
    private const float PreviewHeight = 120f;
    private const float PreviewFontSize = 26f;
    private const float LineSpacing = 36f;
    private const float HighlightPadX = 6f;
    private const float HighlightPadY = 2f;
    private const string Line1 = "Your voice appears";
    private const string Line2Prefix = "right ";
    private const string Line2Highlight = "here";
    private const string Line2Suffix = " while streaming";

    private readonly IDisposable? _changeSubscription;

    private SubtitleOptions _opts;

    public PreviewSection(IOptionsMonitor<SubtitleOptions> monitor)
    {
        _opts = monitor.CurrentValue;
        _changeSubscription = monitor.OnChange((updated, _) => _opts = updated);
    }

    public void Dispose() => _changeSubscription?.Dispose();

    public void Render(float dt)
    {
        using (Ui.Card("##subtitle_preview", padding: 16f))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
            ImGui.TextUnformatted("Preview");
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted(
                "A rough preview of your subtitle styling. The actual font and exact size appear in the stream."
            );
            ImGui.PopStyleColor();
            ImGui.Spacing();

            DrawPreviewCanvas();
        }
    }

    private void DrawPreviewCanvas()
    {
        var avail = ImGui.GetContentRegionAvail();
        var canvasSize = new Vector2(avail.X, PreviewHeight);
        var canvasPos = ImGui.GetCursorScreenPos();

        var drawList = ImGui.GetWindowDrawList();

        // Dark backdrop so the highlight alpha reads correctly.
        var backdrop = new Vector4(0.08f, 0.08f, 0.10f, 1f);
        drawList.AddRectFilled(
            canvasPos,
            canvasPos + canvasSize,
            ImGui.ColorConvertFloat4ToU32(backdrop),
            rounding: 6f
        );

        // Parse configured colors. Fall back to white if the saved hex is malformed —
        // Theme.TryParseHex writes white into the out param on failure, so we can
        // use the value either way.
        Theme.TryParseHex(_opts.Color, out var textColor);
        Theme.TryParseHex(_opts.HighlightColor, out var highlightColor);

        var fillText = ImGui.ColorConvertFloat4ToU32(textColor);
        var fillHighlight = ImGui.ColorConvertFloat4ToU32(highlightColor);
        var stroke = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.6f));
        var highlightBg = ImGui.ColorConvertFloat4ToU32(highlightColor with { W = 0.20f });

        // Scale the actual stroke thickness down to preview proportions. The real
        // stroke is applied at full output resolution; the preview shows the
        // proportional relationship, not the pixel-exact value.
        var strokePx = _opts.StrokeThickness * 0.6f;
        var font = ImGui.GetFont();

        var line1Size = ImGui.CalcTextSize(Line1);
        var line2PrefixSize = ImGui.CalcTextSize(Line2Prefix);
        var line2HighlightSize = ImGui.CalcTextSize(Line2Highlight);
        var line2SuffixSize = ImGui.CalcTextSize(Line2Suffix);
        var line2Width = line2PrefixSize.X + line2HighlightSize.X + line2SuffixSize.X;

        // Vertically center the two-line block within the canvas.
        var totalHeight = LineSpacing * 2f;
        var topY = canvasPos.Y + (canvasSize.Y - totalHeight) * 0.5f;

        // ── Line 1 ─────────────────────────────────────────────────────────────
        var line1Pos = new Vector2(canvasPos.X + (canvasSize.X - line1Size.X) * 0.5f, topY);
        ImGuiHelpers.DrawOutlinedText(
            drawList,
            font,
            PreviewFontSize,
            line1Pos,
            Line1,
            fillText,
            stroke,
            strokePx
        );

        // ── Line 2 ─────────────────────────────────────────────────────────────
        var line2Left = canvasPos.X + (canvasSize.X - line2Width) * 0.5f;
        var line2Y = topY + LineSpacing;

        // Prefix (normal color)
        var prefixPos = new Vector2(line2Left, line2Y);
        ImGuiHelpers.DrawOutlinedText(
            drawList,
            font,
            PreviewFontSize,
            prefixPos,
            Line2Prefix,
            fillText,
            stroke,
            strokePx
        );

        // Highlight chip behind the spoken word
        var highlightPos = new Vector2(line2Left + line2PrefixSize.X, line2Y);
        var highlightBgMin = new Vector2(
            highlightPos.X - HighlightPadX,
            highlightPos.Y - HighlightPadY
        );
        var highlightBgMax = new Vector2(
            highlightPos.X + line2HighlightSize.X + HighlightPadX,
            highlightPos.Y + line2HighlightSize.Y + HighlightPadY
        );
        drawList.AddRectFilled(highlightBgMin, highlightBgMax, highlightBg, rounding: 4f);

        // Highlighted word
        ImGuiHelpers.DrawOutlinedText(
            drawList,
            font,
            PreviewFontSize,
            highlightPos,
            Line2Highlight,
            fillHighlight,
            stroke,
            strokePx
        );

        // Suffix (normal color)
        var suffixPos = new Vector2(line2Left + line2PrefixSize.X + line2HighlightSize.X, line2Y);
        ImGuiHelpers.DrawOutlinedText(
            drawList,
            font,
            PreviewFontSize,
            suffixPos,
            Line2Suffix,
            fillText,
            stroke,
            strokePx
        );

        // ── "Output size" badge ────────────────────────────────────────────────
        var badge = $"Output size: {_opts.FontSize} pt";
        var badgeSize = ImGui.CalcTextSize(badge);
        var badgePos = new Vector2(
            canvasPos.X + canvasSize.X - badgeSize.X - 10f,
            canvasPos.Y + canvasSize.Y - badgeSize.Y - 6f
        );
        drawList.AddText(badgePos, ImGui.ColorConvertFloat4ToU32(Theme.TextSecondary), badge);

        // Advance the ImGui cursor so the next widget doesn't overlap the canvas.
        ImGui.Dummy(canvasSize);
    }
}
