using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels;

/// <summary>
///     Streaming panel: Spout output configuration for OBS integration.
/// </summary>
public sealed class Streaming(
    IOptionsMonitor<AvatarAppConfig> appOptions,
    IConfigWriter configWriter
)
{
    private AvatarAppConfig _appConfig = null!;
    private bool _initialized;

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _appConfig = appOptions.CurrentValue;
        _initialized = true;
    }

    public void Render()
    {
        EnsureInitialized();
        RenderSpoutOutputs();
    }

    // ── Spout Outputs ────────────────────────────────────────────────────────────

    private void RenderSpoutOutputs()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(
            "Spout sends rendered frames to other applications on the same machine. "
                + "Each output below corresponds to a named Spout sender that OBS or other "
                + "compatible software can capture as a video source."
        );
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        var spoutConfigs = _appConfig.SpoutConfigs;

        if (spoutConfigs.Length == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted("No Spout outputs configured.");
            ImGui.PopStyleColor();

            return;
        }

        for (var i = 0; i < spoutConfigs.Length; i++)
        {
            RenderSpoutOutputEntry(spoutConfigs, i);
        }
    }

    private void RenderSpoutOutputEntry(SpoutConfiguration[] spoutConfigs, int index)
    {
        var config = spoutConfigs[index];

        ImGuiHelpers.SectionHeader(config.OutputName);

        var w = config.Width;
        var h = config.Height;

        using var grid = Ui.Grid($"##SpoutDims_{index}", 2);

        grid.Row();
        grid.Col();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted("Width");
        grid.Col();
        ImGui.TextUnformatted("Height");
        ImGui.PopStyleColor();

        grid.Row();
        grid.ColFill();
        if (ImGui.InputInt($"##SpoutW_{index}", ref w))
        {
            w = Math.Max(1, w);
            var updated = config with { Width = w };
            var newConfigs = ReplaceAt(spoutConfigs, index, updated);
            _appConfig = _appConfig with { SpoutConfigs = newConfigs };
            configWriter.Write(_appConfig);
        }

        grid.ColFill();
        if (ImGui.InputInt($"##SpoutH_{index}", ref h))
        {
            h = Math.Max(1, h);
            var updated = config with { Height = h };
            var newConfigs = ReplaceAt(spoutConfigs, index, updated);
            _appConfig = _appConfig with { SpoutConfigs = newConfigs };
            configWriter.Write(_appConfig);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static T[] ReplaceAt<T>(T[] source, int index, T replacement)
    {
        var result = new T[source.Length];
        source.CopyTo(result, 0);
        result[index] = replacement;

        return result;
    }
}
