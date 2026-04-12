using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;

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
        ImGuiHelpers.SectionHeader("Spout Outputs");

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(
            "Spout sends rendered frames to other applications on the same machine. "
                + "Each output below corresponds to a named Spout sender that OBS or other "
                + "compatible software can capture as a video source."
        );
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        ImGui.Spacing();

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
        var childId = $"##SpoutOutput_{index}";
        var childSize = new System.Numerics.Vector2(0f, 90f);

        if (!ImGui.BeginChild(childId, childSize, ImGuiChildFlags.Borders))
        {
            ImGui.EndChild();

            return;
        }

        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.AccentPrimary);
        ImGui.TextUnformatted(config.OutputName);
        ImGui.PopStyleColor();

        ImGui.Spacing();

        // Width
        {
            var width = config.Width;

            ImGuiHelpers.SettingLabel("Width", "Output width in pixels.");

            if (ImGui.InputInt($"##SpoutW_{index}", ref width))
            {
                width = Math.Max(1, width);
                var updated = config with { Width = width };
                var newConfigs = ReplaceAt(spoutConfigs, index, updated);
                _appConfig = _appConfig with { SpoutConfigs = newConfigs };
                configWriter.Write(_appConfig);
            }
        }

        // Height
        {
            var height = config.Height;

            ImGuiHelpers.SettingLabel("Height", "Output height in pixels.");

            if (ImGui.InputInt($"##SpoutH_{index}", ref height))
            {
                height = Math.Max(1, height);
                var updated = config with { Height = height };
                var newConfigs = ReplaceAt(spoutConfigs, index, updated);
                _appConfig = _appConfig with { SpoutConfigs = newConfigs };
                configWriter.Write(_appConfig);
            }
        }

        ImGui.EndChild();
        ImGui.Spacing();
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
