using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Subtitles.Sections;

/// <summary>
///     Font + size + outline emphasis card. Font is picked from a scanned combo
///     over <c>Resources/Fonts</c> via <see cref="ScannedNamePicker" />; a muted
///     "not found" warning appears when the saved font no longer exists on disk.
/// </summary>
public sealed class TextStyleSection : IDisposable
{
    private readonly IConfigWriter _configWriter;
    private readonly FontProvider _fontProvider;
    private readonly IDisposable? _changeSubscription;
    private readonly ScannedNamePicker _picker;

    private SubtitleOptions _opts;
    private bool _initialized;

    public TextStyleSection(
        IOptionsMonitor<SubtitleOptions> monitor,
        FontProvider fontProvider,
        IConfigWriter configWriter
    )
    {
        _configWriter = configWriter;
        _fontProvider = fontProvider;
        _opts = monitor.CurrentValue;
        _picker = new ScannedNamePicker(() => _fontProvider.GetAvailableFonts());

        _changeSubscription = monitor.OnChange(
            (updated, _) =>
            {
                var fontChanged = !string.Equals(
                    _opts.Font,
                    updated.Font,
                    StringComparison.Ordinal
                );
                _opts = updated;
                if (!_initialized)
                    return;
                if (fontChanged)
                    _picker.RecomputeMissing(_opts.Font);
            }
        );
    }

    public void Dispose() => _changeSubscription?.Dispose();

    public void Render(float dt)
    {
        if (!_initialized)
        {
            _picker.Refresh(_opts.Font);
            _initialized = true;
        }

        using (Ui.Card("##text_style", padding: 12f))
        {
            // Header
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
            ImGui.TextUnformatted("Text Style");
            ImGui.PopStyleColor();

            // Description
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted("Font, size, and how thick the outline around your text is.");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            RenderFontRow();
            RenderSizeRow(dt);
            RenderOutlineRow(dt);
        }
    }

    // ── Font row ──────────────────────────────────────────────────────────────

    private void RenderFontRow()
    {
        var rowY = ImGui.GetCursorPosY();

        ImGuiHelpers.SettingLabel("Font", "A TrueType font from your Resources/Fonts folder.");

        if (
            ImGuiHelpers.ScannedCombo(
                "SubtitleFont",
                _picker,
                _opts.Font,
                out var picked,
                onRefresh: () => _picker.Refresh(_opts.Font),
                refreshTooltip: "Re-scan Resources/Fonts for TrueType fonts."
            )
        )
        {
            _opts = _opts with { Font = picked };
            _configWriter.Write(_opts);
            _picker.RecomputeMissing(_opts.Font);
        }

        if (_picker.IsMissing)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted($"'{_opts.Font}' not found in Resources/Fonts");
            ImGui.PopStyleColor();
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }

    // ── Size row ──────────────────────────────────────────────────────────────

    private void RenderSizeRow(float dt)
    {
        var rowY = ImGui.GetCursorPosY();

        ImGuiHelpers.SettingLabel("Size", "Point size the subtitle text is rendered at.");

        var size = _opts.FontSize;
        if (
            ImGuiHelpers.LabeledSlider("##SubtitleFontSize", ref size, 24, 200, "Small", "Huge", dt)
        )
        {
            _opts = _opts with { FontSize = size };
            _configWriter.Write(_opts);
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }

    // ── Outline row ───────────────────────────────────────────────────────────

    private void RenderOutlineRow(float dt)
    {
        var rowY = ImGui.GetCursorPosY();

        ImGuiHelpers.SettingLabel(
            "Outline",
            "A darker stroke around each letter, to pop the text against busy backgrounds."
        );

        var stroke = _opts.StrokeThickness;
        if (ImGuiHelpers.LabeledSlider("##SubtitleStroke", ref stroke, 0, 10, "None", "Bold", dt))
        {
            _opts = _opts with { StrokeThickness = stroke };
            _configWriter.Write(_opts);
        }

        ImGuiHelpers.SettingEndRow(rowY);
    }
}
