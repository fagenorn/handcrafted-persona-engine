using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Subtitles.Sections;

/// <summary>
///     Spout output resolution. Performer-facing — stream canvas presets only
///     (via <see cref="ImGuiHelpers.ResolutionChips" />). Matches the Avatar panel.
/// </summary>
public sealed class CanvasSection : IDisposable
{
    private readonly IConfigWriter _configWriter;
    private readonly IDisposable? _changeSubscription;

    private SubtitleOptions _opts;

    public CanvasSection(IOptionsMonitor<SubtitleOptions> monitor, IConfigWriter configWriter)
    {
        _configWriter = configWriter;
        _opts = monitor.CurrentValue;
        _changeSubscription = monitor.OnChange((updated, _) => _opts = updated);
    }

    public void Dispose() => _changeSubscription?.Dispose();

    public void Render(float dt)
    {
        using (Ui.Card("##canvas", padding: 12f))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
            ImGui.TextUnformatted("Output Canvas");
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted(
                "The Spout texture size sent to OBS. Pick the orientation, then a preset that matches your scene."
            );
            ImGui.PopStyleColor();
            ImGui.Spacing();

            var rowY = ImGui.GetCursorPosY();

            ImGuiHelpers.SettingLabel(
                "Resolution",
                "Canvas size for the subtitle overlay. Match your stream scene in OBS."
            );

            var width = _opts.Width;
            var height = _opts.Height;

            if (ImGuiHelpers.ResolutionChips("SubtitleRes", ref width, ref height))
            {
                _opts = _opts with { Width = width, Height = height };
                _configWriter.Write(_opts);
            }

            ImGuiHelpers.SettingEndRow(rowY);
        }
    }
}
