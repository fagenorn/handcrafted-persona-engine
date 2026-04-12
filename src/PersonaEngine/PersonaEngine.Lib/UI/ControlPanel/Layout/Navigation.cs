using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

public enum NavSection
{
    Dashboard,
    Voice,
    Personality,
    Listening,
    Avatar,
    Subtitles,
    RouletteWheel,
    ScreenAwareness,
    Streaming,
    LlmConnection,
    Application,
}

/// <summary>
///     Sidebar navigation component for the control panel.
/// </summary>
public sealed class Navigation
{
    private static readonly (NavSection Section, string Label)[] _sections =
    [
        (NavSection.Dashboard, "Dashboard"),
        (NavSection.Voice, "Voice"),
        (NavSection.Personality, "Personality"),
        (NavSection.Listening, "Listening"),
        (NavSection.Avatar, "Avatar"),
        (NavSection.Subtitles, "Subtitles"),
        (NavSection.RouletteWheel, "Roulette Wheel"),
        (NavSection.ScreenAwareness, "Screen Aware"),
        (NavSection.Streaming, "Streaming"),
        (NavSection.LlmConnection, "LLM Connection"),
        (NavSection.Application, "Application"),
    ];

    public NavSection ActiveSection { get; private set; } = NavSection.Dashboard;

    public void Render()
    {
        var drawList = ImGui.GetWindowDrawList();

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

            ImGui.PopStyleColor(isActive ? 3 : 1);

            if (isActive)
            {
                var itemMin = ImGui.GetItemRectMin();
                var itemMax = ImGui.GetItemRectMax();
                var accentMin = itemMin;
                var accentMax = new Vector2(itemMin.X + 4f, itemMax.Y);
                var accentCol = ImGui.ColorConvertFloat4ToU32(Theme.AccentPrimary);

                ImGui.AddRectFilled(drawList, accentMin, accentMax, accentCol);
            }
        }
    }
}
