using System.Numerics;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Subtitles.Sections;

/// <summary>
///     Base text color + highlight (current word) color. Both are stored as
///     <c>"#AARRGGBB"</c> strings and round-tripped through
///     <see cref="Theme.TryParseHex" /> / <see cref="Theme.ToHexString" />.
/// </summary>
public sealed class ColorsSection : IDisposable
{
    private readonly IConfigWriter _configWriter;
    private readonly IDisposable? _changeSubscription;

    private SubtitleOptions _opts;

    public ColorsSection(IOptionsMonitor<SubtitleOptions> monitor, IConfigWriter configWriter)
    {
        _configWriter = configWriter;
        _opts = monitor.CurrentValue;
        _changeSubscription = monitor.OnChange((updated, _) => _opts = updated);
    }

    public void Dispose() => _changeSubscription?.Dispose();

    public void Render(float dt)
    {
        using (Ui.Card("##colors", padding: 12f))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
            ImGui.TextUnformatted("Colors");
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted(
                "The base text color and the highlight shown on the currently spoken word."
            );
            ImGui.PopStyleColor();
            ImGui.Spacing();

            RenderTextColorRow();
            RenderHighlightColorRow();
        }
    }

    private void RenderTextColorRow()
    {
        var rowY = ImGui.GetCursorPosY();

        ImGuiHelpers.SettingLabel("Text", "Base color for subtitle text.");

        Theme.TryParseHex(_opts.Color, out var vec);
        if (ImGui.ColorEdit4("##SubtitleColor", ref vec, ImGuiColorEditFlags.AlphaBar))
        {
            _opts = _opts with { Color = Theme.ToHexString(vec) };
            _configWriter.Write(_opts);
        }

        ImGuiHelpers.HandCursorOnHover();
        ImGuiHelpers.SettingEndRow(rowY);
    }

    private void RenderHighlightColorRow()
    {
        var rowY = ImGui.GetCursorPosY();

        ImGuiHelpers.SettingLabel("Highlight", "Color used for the currently spoken word.");

        Theme.TryParseHex(_opts.HighlightColor, out var vec);
        if (ImGui.ColorEdit4("##SubtitleHighlight", ref vec, ImGuiColorEditFlags.AlphaBar))
        {
            _opts = _opts with { HighlightColor = Theme.ToHexString(vec) };
            _configWriter.Write(_opts);
        }

        ImGuiHelpers.HandCursorOnHover();
        ImGuiHelpers.SettingEndRow(rowY);
    }
}
