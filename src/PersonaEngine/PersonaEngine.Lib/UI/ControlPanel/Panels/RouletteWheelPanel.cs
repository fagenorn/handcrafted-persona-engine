using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
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
    private bool _initialized;

    // Playground state
    private int _playgroundTargetIndex;
    private string _newLabelBuffer = string.Empty;

    private RouletteWheelOptions _opts;

    public void Render()
    {
        EnsureInitialized();
        RenderPlaygroundSection();
        RenderSectionsSection();
        RenderSizeSection();
        RenderSpinBehaviorSection();
        RenderTextStyleSection();
    }

    // ── Initialization ───────────────────────────────────────────────────────────

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _opts = wheelOptions.CurrentValue;
        _fonts = fontProvider.GetAvailableFonts().ToArray();
        _initialized = true;
    }

    // ── Playground section ───────────────────────────────────────────────────────

    private void RenderPlaygroundSection()
    {
        ImGuiHelpers.SectionHeader("Playground");

        var labels = _opts.SectionLabels;

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
        if (ImGuiHelpers.PrimaryButton("Spin to Target"))
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
            _opts = _opts with { Enabled = !_opts.Enabled };
            configWriter.Write(_opts);
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

        var labels = _opts.SectionLabels.ToList();
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

            if (ImGuiHelpers.DangerButton($"X##WheelRemove{i}"))
            {
                labels.RemoveAt(i);
                if (_playgroundTargetIndex >= labels.Count && labels.Count > 0)
                    _playgroundTargetIndex = labels.Count - 1;
                changed = true;
                break; // indices invalidated — re-render next frame
            }
        }

        ImGui.Spacing();

        // New label input + Add button
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 60f);
        ImGui.InputText("##WheelNewLabel", ref _newLabelBuffer, 128);
        ImGui.SameLine();

        if (ImGuiHelpers.PrimaryButton("Add##WheelAdd"))
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
            _opts = _opts with { SectionLabels = [.. labels] };
            configWriter.Write(_opts);
        }
    }

    // ── Size section ─────────────────────────────────────────────────────────────

    private void RenderSizeSection()
    {
        ImGuiHelpers.SectionHeader("Size");

        {
            using var grid = Ui.Grid("##WheelDims", 2);

            grid.Row();
            grid.Col();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted("Width");
            grid.Col();
            ImGui.TextUnformatted("Height");
            ImGui.PopStyleColor();

            grid.Row();
            grid.ColFill();
            var width = _opts.Width;
            if (ImGui.InputInt("##WheelWidth", ref width))
            {
                width = Math.Max(1, width);
                _opts = _opts with { Width = width };
                configWriter.Write(_opts);
            }

            grid.ColFill();
            var height = _opts.Height;
            if (ImGui.InputInt("##WheelHeight", ref height))
            {
                height = Math.Max(1, height);
                _opts = _opts with { Height = height };
                configWriter.Write(_opts);
            }
        }
    }

    // ── Spin Behavior section ────────────────────────────────────────────────────

    private void RenderSpinBehaviorSection()
    {
        ImGuiHelpers.SectionHeader("Spin Behavior");

        // Spin Duration
        {
            var duration = _opts.SpinDuration;

            ImGuiHelpers.SettingLabel(
                "Spin Duration",
                "How long the wheel spins before stopping, in seconds (1–15)."
            );

            if (ImGui.SliderFloat("##WheelSpinDuration", ref duration, 1f, 15f, "%.1f s"))
            {
                _opts = _opts with { SpinDuration = duration };
                configWriter.Write(_opts);
            }

            ImGuiHelpers.SliderGlow();
        }

        // Min Rotations
        {
            var minRotations = _opts.MinRotations;

            ImGuiHelpers.SettingLabel(
                "Min Rotations",
                "Minimum number of full rotations during a spin (1–20)."
            );

            if (ImGui.SliderFloat("##WheelMinRot", ref minRotations, 1f, 20f, "%.0f"))
            {
                _opts = _opts with { MinRotations = minRotations };
                configWriter.Write(_opts);
            }

            ImGuiHelpers.SliderGlow();
        }

        // Animate Show/Hide
        {
            var animate = _opts.AnimateToggle;

            ImGuiHelpers.SettingLabel(
                "Animate Show/Hide",
                "Smoothly animate the wheel when showing or hiding it."
            );

            if (ImGui.Checkbox("##WheelAnimateToggle", ref animate))
            {
                _opts = _opts with { AnimateToggle = animate };
                configWriter.Write(_opts);
            }
        }

        // Visible by Default
        {
            var enabled = _opts.Enabled;

            ImGuiHelpers.SettingLabel(
                "Visible by Default",
                "Whether the wheel starts visible when the application launches."
            );

            if (ImGui.Checkbox("##WheelEnabled", ref enabled))
            {
                _opts = _opts with { Enabled = enabled };
                configWriter.Write(_opts);
            }
        }
    }

    // ── Text Style section ───────────────────────────────────────────────────────

    private void RenderTextStyleSection()
    {
        ImGuiHelpers.SectionHeader("Text Style");

        // Font combo
        {
            var currentIndex = Array.IndexOf(_fonts, _opts.Font);
            if (currentIndex < 0)
                currentIndex = 0;

            ImGuiHelpers.SettingLabel("Font", "The font file used to render section labels.");

            if (ImGui.Combo("##WheelFont", ref currentIndex, _fonts, _fonts.Length))
            {
                var selected = _fonts.Length > 0 ? _fonts[currentIndex] : _opts.Font;
                _opts = _opts with { Font = selected };
                configWriter.Write(_opts);
            }
        }

        // Font Size
        {
            var fontSize = _opts.FontSize;

            ImGuiHelpers.SettingLabel(
                "Font Size",
                "Base font size for section label text (8–200)."
            );

            if (ImGui.SliderInt("##WheelFontSize", ref fontSize, 8, 200))
            {
                _opts = _opts with { FontSize = fontSize };
                configWriter.Write(_opts);
            }

            ImGuiHelpers.SliderGlow();
        }

        // Text Scale
        {
            var scale = _opts.TextScale;

            ImGuiHelpers.SettingLabel(
                "Text Scale",
                "Uniform scale multiplier applied to rendered label text (0.5–2.0)."
            );

            if (ImGui.SliderFloat("##WheelTextScale", ref scale, 0.5f, 2.0f, "%.2f"))
            {
                _opts = _opts with { TextScale = scale };
                configWriter.Write(_opts);
            }

            ImGuiHelpers.SliderGlow();
        }
    }
}
