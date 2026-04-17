using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

public enum NavSection
{
    Dashboard,
    LlmConnection,
    Personality,
    Listening,
    Voice,
    Avatar,
    Subtitles,
    Overlay,
    Streaming,
    RouletteWheel,
    ScreenAwareness,
    Application,
}

/// <summary>
///     Sidebar navigation component with breathing gradient fill and hover warmth.
/// </summary>
public sealed class Navigation
{
    private static readonly (NavSection Section, string Label)[] _sections =
    [
        (NavSection.Dashboard, "Dashboard"),
        (NavSection.LlmConnection, "LLM Connection"),
        (NavSection.Personality, "Personality"),
        (NavSection.Listening, "Listening"),
        (NavSection.Voice, "Voice"),
        (NavSection.Avatar, "Avatar"),
        (NavSection.Subtitles, "Subtitles"),
        (NavSection.Overlay, "Overlay"),
        (NavSection.Streaming, "Streaming"),
        (NavSection.RouletteWheel, "Roulette Wheel"),
        (NavSection.ScreenAwareness, "Screen Aware"),
        (NavSection.Application, "Application"),
    ];

    // Breathing animation for active item gradient (alpha in [0.06, 0.10])
    private readonly SineOscillator _breatheIdle = new(
        center: 0.08f,
        amplitude: 0.02f,
        frequencyHz: 0.25f
    );
    private readonly SineOscillator _breatheActive = new(
        center: 0.09f,
        amplitude: 0.02f,
        frequencyHz: 0.4f
    );

    // Accent border pulse (alpha in [0.6, 1.0])
    private readonly SineOscillator _borderPulse = new(
        center: 0.8f,
        amplitude: 0.2f,
        frequencyHz: 0.8f
    );

    // State transition flare
    private OneShotAnimation _borderFlare;

    private float _elapsed;

    public NavSection ActiveSection { get; private set; } = NavSection.Dashboard;

    /// <summary>
    ///     Programmatically switches the active sidebar section. Used by
    ///     <see cref="INavRequestBus" /> subscribers (dashboard health cards,
    ///     cross-panel hint links) to mirror a user tab click.
    /// </summary>
    public void SetActiveSection(NavSection section) => ActiveSection = section;

    public void Render(float deltaTime, PersonaStateProvider stateProvider)
    {
        _elapsed += deltaTime;
        _borderFlare.Update(deltaTime);

        if (stateProvider.StateJustChanged)
        {
            _borderFlare.Start(0.2f);
        }

        var drawList = ImGui.GetWindowDrawList();
        var isSpeaking = stateProvider.State == PersonaUiState.Speaking;

        foreach (var (section, label) in _sections)
        {
            var isActive = section == ActiveSection;

            if (isActive)
            {
                ImGui.PushStyleColor(ImGuiCol.Header, Theme.Surface2);
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Surface2);
                ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.Surface2);
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.SurfaceHover);
            }

            if (ImGui.Selectable(label, isActive, ImGuiSelectableFlags.None, new Vector2(0f, 0f)))
            {
                ActiveSection = section;
            }

            ImGuiHelpers.HandCursorOnHover();
            ImGui.PopStyleColor(isActive ? 3 : 1);

            var itemMin = ImGui.GetItemRectMin();
            var itemMax = ImGui.GetItemRectMax();

            if (isActive)
            {
                // Breathing gradient fill behind active item
                var breatheAlpha = (isSpeaking ? _breatheActive : _breatheIdle).Sample(_elapsed);
                var gradientLeft = ImGui.ColorConvertFloat4ToU32(
                    Theme.AccentPrimary with
                    {
                        W = breatheAlpha,
                    }
                );
                var gradientRight = ImGui.ColorConvertFloat4ToU32(
                    Theme.AccentPrimary with
                    {
                        W = 0f,
                    }
                );
                var fadeEnd = itemMin.X + (itemMax.X - itemMin.X) * 0.6f;

                ImGui.AddRectFilledMultiColor(
                    drawList,
                    itemMin,
                    new Vector2(fadeEnd, itemMax.Y),
                    gradientLeft,
                    gradientRight,
                    gradientRight,
                    gradientLeft
                );

                // Pulsing accent border
                var borderAlpha = _borderPulse.Sample(_elapsed);

                // Flare on state transition: brief brightness boost
                if (_borderFlare.IsActive)
                {
                    var flareT = Easing.EaseOutCubic(_borderFlare.Progress);
                    borderAlpha = MathF.Min(borderAlpha + (1f - flareT) * 0.4f, 1f);
                }

                var accentMin = new Vector2(ImGui.GetWindowPos().X, itemMin.Y);
                var accentMax = new Vector2(ImGui.GetWindowPos().X + 4f, itemMax.Y);
                var accentCol = ImGui.ColorConvertFloat4ToU32(
                    Theme.AccentPrimary with
                    {
                        W = borderAlpha,
                    }
                );

                ImGui.AddRectFilled(drawList, accentMin, accentMax, accentCol);
            }
            else if (ImGui.IsItemHovered())
            {
                // Hover warmth: subtle rounded-rect tint behind the item
                var glowCol = ImGui.ColorConvertFloat4ToU32(Theme.AccentPrimary with { W = 0.05f });
                ImGui.AddRectFilled(
                    drawList,
                    itemMin,
                    itemMax,
                    glowCol,
                    ImGui.GetStyle().FrameRounding
                );
            }
        }
    }
}
