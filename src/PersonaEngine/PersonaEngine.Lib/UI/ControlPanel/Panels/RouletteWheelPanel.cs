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
    // Shared placeholder array for the disabled "(no sections)" combo. Lifting it
    // to a static field avoids the per-frame collection-expression allocation.
    private static readonly string[] NoSectionsPlaceholder = ["(no sections)"];

    private string[] _fonts = [];
    private bool _initialized;

    // Playground state
    private int _playgroundTargetIndex;
    private string _newLabelBuffer = string.Empty;

    // Scratch list reused across Sections-section edits so the editor doesn't
    // allocate a fresh List<string> every frame. Only mutated while the user is
    // actively editing; SectionLabels remains the source of truth.
    private readonly List<string> _sectionEditBuffer = [];

    private RouletteWheelOptions _opts;

    private AnimatedFloat _animateToggleKnob;
    private AnimatedFloat _enabledKnob;

    public void Render(float deltaTime)
    {
        EnsureInitialized();
        RenderPlaygroundSection();
        RenderSectionsSection();
        RenderSizeSection();
        RenderSpinBehaviorSection(deltaTime);
        RenderTextStyleSection();
    }

    // ── Initialization ───────────────────────────────────────────────────────────

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _opts = wheelOptions.CurrentValue;
        _fonts = fontProvider.GetAvailableFonts().ToArray();

        _animateToggleKnob = new AnimatedFloat(_opts.AnimateToggle ? 1f : 0f);
        _enabledKnob = new AnimatedFloat(_opts.Enabled ? 1f : 0f);

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
                ImGui.Combo(
                    "##WheelPlayTarget",
                    ref _playgroundTargetIndex,
                    NoSectionsPlaceholder,
                    1
                );
                ImGuiHelpers.HandCursorOnHover();
                ImGui.EndDisabled();
            }
            else
            {
                if (_playgroundTargetIndex >= labels.Length)
                    _playgroundTargetIndex = 0;

                ImGui.Combo("##WheelPlayTarget", ref _playgroundTargetIndex, labels, labels.Length);
                ImGuiHelpers.HandCursorOnHover();
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

        ImGuiHelpers.HandCursorOnHover();
        ImGui.SameLine();

        // Show / Hide toggle
        var toggleLabel = wheel.IsEnabled ? "Hide" : "Show";
        if (ImGui.Button(toggleLabel))
        {
            wheel.Toggle();
            _opts = _opts with { Enabled = !_opts.Enabled };
            configWriter.Write(_opts);
        }

        ImGuiHelpers.HandCursorOnHover();

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

        // Reuse the scratch buffer instead of allocating a new List<string> on
        // every frame. It's refilled from SectionLabels each frame so edits are
        // always applied against the current config snapshot.
        var labels = _sectionEditBuffer;
        labels.Clear();
        var source = _opts.SectionLabels;
        for (var i = 0; i < source.Length; i++)
        {
            labels.Add(source[i]);
        }
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
            var width = _opts.Width;
            var height = _opts.Height;

            ImGuiHelpers.SettingLabel("Resolution", "Output resolution for the wheel.");

            if (ImGuiHelpers.ResolutionPicker("WheelRes", ref width, ref height, square: true))
            {
                _opts = _opts with { Width = width, Height = height };
                configWriter.Write(_opts);
            }
        }
    }

    // ── Spin Behavior section ────────────────────────────────────────────────────

    private void RenderSpinBehaviorSection(float dt)
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

            if (
                ImGuiHelpers.ToggleSwitch(
                    "##WheelAnimateToggle",
                    ref animate,
                    ref _animateToggleKnob,
                    dt
                )
            )
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

            if (ImGuiHelpers.ToggleSwitch("##WheelEnabled", ref enabled, ref _enabledKnob, dt))
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

            ImGuiHelpers.HandCursorOnHover();
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
