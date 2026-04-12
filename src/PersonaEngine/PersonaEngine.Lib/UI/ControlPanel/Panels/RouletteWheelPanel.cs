using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using WheelRenderer = PersonaEngine.Lib.UI.Rendering.RouletteWheel.RouletteWheel;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels;

/// <summary>
///     Roulette Wheel panel: playground controls, section labels, size, spin behaviour, and text style.
/// </summary>
public sealed class RouletteWheelPanel(
    IOptionsMonitor<RouletteWheelOptions> wheelOptions,
    WheelRenderer wheel,
    FontProvider fontProvider,
    IConfigWriter configWriter
)
{
    private string[] _fonts = [];
    private bool _fontsLoaded;

    // Playground state
    private int _playgroundTargetIndex;
    private string _newLabelBuffer = string.Empty;

    public void Render()
    {
        EnsureFontsLoaded();
        RenderPlaygroundSection();
        RenderSectionsSection();
        RenderSizeSection();
        RenderSpinBehaviorSection();
        RenderTextStyleSection();
    }

    // ── Font loading ─────────────────────────────────────────────────────────────

    private void EnsureFontsLoaded()
    {
        if (_fontsLoaded)
            return;

        _fonts = fontProvider.GetAvailableFontsAsync().GetAwaiter().GetResult().ToArray();
        _fontsLoaded = true;
    }

    // ── Playground section ───────────────────────────────────────────────────────

    private void RenderPlaygroundSection()
    {
        ImGuiHelpers.SectionHeader("Playground");

        var opts = wheelOptions.CurrentValue;
        var labels = opts.SectionLabels;

        // Target section combo
        {
            ImGuiHelpers.SettingLabel("Target Section", "The section to land on when spinning.");

            if (labels.Length == 0)
            {
                ImGui.BeginDisabled();
                var noSections = "(no sections)";
                ImGui.Combo("##WheelPlayTarget", ref _playgroundTargetIndex, [noSections], 1);
                ImGui.EndDisabled();
            }
            else
            {
                if (_playgroundTargetIndex >= labels.Length)
                    _playgroundTargetIndex = 0;

                ImGui.Combo("##WheelPlayTarget", ref _playgroundTargetIndex, labels, labels.Length);
            }
        }

        ImGui.Spacing();

        var spinning = wheel.IsSpinning;
        var noLabels = labels.Length == 0;

        if (spinning || noLabels)
            ImGui.BeginDisabled();

        // Spin to target
        if (ImGui.Button("Spin to Target"))
        {
            _ = wheel.SpinAsync(_playgroundTargetIndex);
        }

        ImGui.SameLine();

        // Random spin
        if (ImGui.Button("Random"))
        {
            _ = wheel.SpinAsync();
        }

        ImGui.SameLine();

        // Show / Hide toggle
        var toggleLabel = wheel.IsEnabled ? "Hide" : "Show";
        if (ImGui.Button(toggleLabel))
        {
            wheel.Toggle();
            var updated = opts with { Enabled = !opts.Enabled };
            configWriter.Write(updated);
        }

        if (spinning || noLabels)
            ImGui.EndDisabled();

        if (spinning)
        {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted("Spinning…");
            ImGui.PopStyleColor();
        }
    }

    // ── Sections section ─────────────────────────────────────────────────────────

    private void RenderSectionsSection()
    {
        ImGuiHelpers.SectionHeader("Sections");

        var opts = wheelOptions.CurrentValue;
        var labels = opts.SectionLabels.ToList();
        var changed = false;

        for (var i = 0; i < labels.Count; i++)
        {
            var label = labels[i];
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 40f);

            if (ImGui.InputText($"##WheelSection{i}", ref label, 128))
            {
                labels[i] = label;
                changed = true;
            }

            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Error with { W = 0.6f });
            if (ImGui.Button($"X##WheelRemove{i}"))
            {
                labels.RemoveAt(i);
                if (_playgroundTargetIndex >= labels.Count && labels.Count > 0)
                    _playgroundTargetIndex = labels.Count - 1;
                changed = true;
                ImGui.PopStyleColor();
                break; // indices invalidated — re-render next frame
            }

            ImGui.PopStyleColor();
        }

        ImGui.Spacing();

        // New label input + Add button
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 60f);
        ImGui.InputText("##WheelNewLabel", ref _newLabelBuffer, 128);
        ImGui.SameLine();

        if (ImGui.Button("Add##WheelAdd"))
        {
            var newLabel = _newLabelBuffer.Trim();
            if (!string.IsNullOrEmpty(newLabel))
            {
                labels.Add(newLabel);
                _newLabelBuffer = string.Empty;
                changed = true;
            }
        }

        if (changed)
        {
            configWriter.Write(opts with { SectionLabels = [.. labels] });
        }
    }

    // ── Size section ─────────────────────────────────────────────────────────────

    private void RenderSizeSection()
    {
        ImGuiHelpers.SectionHeader("Size");

        var opts = wheelOptions.CurrentValue;

        // Width
        {
            var width = opts.Width;

            ImGuiHelpers.SettingLabel("Width", "Width of the wheel output in pixels.");

            if (ImGui.InputInt("##WheelWidth", ref width))
            {
                width = Math.Max(1, width);
                configWriter.Write(opts with { Width = width });
            }
        }

        // Height
        {
            var height = opts.Height;

            ImGuiHelpers.SettingLabel("Height", "Height of the wheel output in pixels.");

            if (ImGui.InputInt("##WheelHeight", ref height))
            {
                height = Math.Max(1, height);
                configWriter.Write(opts with { Height = height });
            }
        }
    }

    // ── Spin Behavior section ────────────────────────────────────────────────────

    private void RenderSpinBehaviorSection()
    {
        ImGuiHelpers.SectionHeader("Spin Behavior");

        var opts = wheelOptions.CurrentValue;

        // Spin Duration
        {
            var duration = opts.SpinDuration;

            ImGuiHelpers.SettingLabel(
                "Spin Duration",
                "How long the wheel spins before stopping, in seconds (1–15)."
            );

            if (ImGui.SliderFloat("##WheelSpinDuration", ref duration, 1f, 15f, "%.1f s"))
            {
                configWriter.Write(opts with { SpinDuration = duration });
            }

            ImGuiHelpers.SliderGlow();
        }

        // Min Rotations
        {
            var minRotations = opts.MinRotations;

            ImGuiHelpers.SettingLabel(
                "Min Rotations",
                "Minimum number of full rotations during a spin (1–20)."
            );

            if (ImGui.SliderFloat("##WheelMinRot", ref minRotations, 1f, 20f, "%.0f"))
            {
                configWriter.Write(opts with { MinRotations = minRotations });
            }

            ImGuiHelpers.SliderGlow();
        }

        // Animate Show/Hide
        {
            var animate = opts.AnimateToggle;

            ImGuiHelpers.SettingLabel(
                "Animate Show/Hide",
                "Smoothly animate the wheel when showing or hiding it."
            );

            if (ImGui.Checkbox("##WheelAnimateToggle", ref animate))
            {
                configWriter.Write(opts with { AnimateToggle = animate });
            }
        }

        // Visible by Default
        {
            var enabled = opts.Enabled;

            ImGuiHelpers.SettingLabel(
                "Visible by Default",
                "Whether the wheel starts visible when the application launches."
            );

            if (ImGui.Checkbox("##WheelEnabled", ref enabled))
            {
                configWriter.Write(opts with { Enabled = enabled });
            }
        }
    }

    // ── Text Style section ───────────────────────────────────────────────────────

    private void RenderTextStyleSection()
    {
        ImGuiHelpers.SectionHeader("Text Style");

        var opts = wheelOptions.CurrentValue;

        // Font combo
        {
            var currentIndex = Array.IndexOf(_fonts, opts.Font);
            if (currentIndex < 0)
                currentIndex = 0;

            ImGuiHelpers.SettingLabel("Font", "The font file used to render section labels.");

            if (ImGui.Combo("##WheelFont", ref currentIndex, _fonts, _fonts.Length))
            {
                var selected = _fonts.Length > 0 ? _fonts[currentIndex] : opts.Font;
                configWriter.Write(opts with { Font = selected });
            }
        }

        // Font Size
        {
            var fontSize = opts.FontSize;

            ImGuiHelpers.SettingLabel(
                "Font Size",
                "Base font size for section label text (8–200)."
            );

            if (ImGui.SliderInt("##WheelFontSize", ref fontSize, 8, 200))
            {
                configWriter.Write(opts with { FontSize = fontSize });
            }

            ImGuiHelpers.SliderGlow();
        }

        // Text Scale
        {
            var scale = opts.TextScale;

            ImGuiHelpers.SettingLabel(
                "Text Scale",
                "Uniform scale multiplier applied to rendered label text (0.5–2.0)."
            );

            if (ImGui.SliderFloat("##WheelTextScale", ref scale, 0.5f, 2.0f, "%.2f"))
            {
                configWriter.Write(opts with { TextScale = scale });
            }

            ImGuiHelpers.SliderGlow();
        }
    }
}
