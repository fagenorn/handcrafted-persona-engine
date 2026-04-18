using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
///     Themed button widgets layered on top of <see cref="ImGui.Button(string)"/>.
///     Each variant pushes the appropriate <see cref="Theme"/> colours, applies
///     <see cref="ImGuiHelpers.HandCursorOnHover"/>, and pops the style stack.
/// </summary>
public static partial class ImGuiHelpers
{
    /// <summary>
    ///     Renders an accent-colored button for primary actions.
    /// </summary>
    public static bool PrimaryButton(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.AccentPrimary with { W = 0.7f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentPrimary with { W = 0.85f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentPrimary);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Base);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(4);
        HandCursorOnHover();
        return clicked;
    }

    /// <summary>
    ///     Renders a primary button with press feedback (brief darkening)
    ///     and a warm glow pulse on click.
    /// </summary>
    public static bool PrimaryButtonWithFeedback(
        string label,
        ref OneShotAnimation pressAnim,
        float dt
    )
    {
        pressAnim.Update(dt);

        // Darken slightly on press (non-linear pulse)
        var baseAlpha = 0.7f;
        if (pressAnim.IsActive)
        {
            var pressT = pressAnim.Progress;
            var darken = (pressT < 0.3f ? pressT / 0.3f : (1f - pressT) / 0.7f) * 0.05f;
            baseAlpha = 0.7f - darken;
        }

        ImGui.PushStyleColor(ImGuiCol.Button, Theme.AccentPrimary with { W = baseAlpha });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentPrimary with { W = 0.85f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentPrimary);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Base);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(4);
        HandCursorOnHover();

        if (clicked)
        {
            pressAnim.Start(0.15f);

            // Warm glow pulse around the button on click
            var drawList = ImGui.GetWindowDrawList();
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var glowCol = ImGui.ColorConvertFloat4ToU32(Theme.AccentPrimary with { W = 0.15f });
            var padding = new Vector2(4f, 4f);
            ImGui.AddRectFilled(
                drawList,
                min - padding,
                max + padding,
                glowCol,
                ImGui.GetStyle().FrameRounding + 2f
            );
        }

        return clicked;
    }

    /// <summary>
    ///     Renders a low-emphasis button for secondary actions like "Reset defaults".
    ///     When <paramref name="enabled" /> is <see langword="false" /> the button is
    ///     dimmed and non-interactive; when <see langword="true" /> it uses secondary
    ///     text color with surface-hover feedback so it's clearly clickable.
    /// </summary>
    public static bool SubtleButton(string label, bool enabled = true)
    {
        if (!enabled)
            ImGui.BeginDisabled();

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f, 5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Surface3);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.SurfaceHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentPrimary with { W = 0.4f });
        ImGui.PushStyleColor(ImGuiCol.Text, enabled ? Theme.TextSecondary : Theme.TextTertiary);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(2);

        if (!enabled)
            ImGui.EndDisabled();
        else
            HandCursorOnHover();

        return clicked;
    }

    /// <summary>
    ///     Renders an error-colored button for destructive actions.
    /// </summary>
    public static bool DangerButton(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Error with { W = 0.6f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Error with { W = 0.8f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Error);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(3);
        HandCursorOnHover();
        return clicked;
    }
}
