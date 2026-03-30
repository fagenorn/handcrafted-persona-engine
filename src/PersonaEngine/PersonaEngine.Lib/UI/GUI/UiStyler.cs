using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Static utility class for common UI styling operations
/// </summary>
public static class UiStyler
{
    /// <summary>
    ///     Executes an action with a temporary style color change
    /// </summary>
    public static void WithStyleColor(ImGuiCol colorId, Vector4 color, Action action)
    {
        ImGui.PushStyleColor(colorId, color);
        try
        {
            action();
        }
        finally
        {
            ImGui.PopStyleColor();
        }
    }

    /// <summary>
    ///     Executes an action with multiple temporary style color changes
    /// </summary>
    public static void WithStyleColors(IReadOnlyList<(ImGuiCol, Vector4)> colors, Action action)
    {
        foreach (var (colorId, color) in colors)
        {
            ImGui.PushStyleColor(colorId, color);
        }

        try
        {
            action();
        }
        finally
        {
            ImGui.PopStyleColor(colors.Count);
        }
    }

    /// <summary>
    ///     Executes an action with a temporary style variable change
    /// </summary>
    public static void WithStyleVar(ImGuiStyleVar styleVar, Vector2 value, Action action)
    {
        ImGui.PushStyleVar(styleVar, value);
        try
        {
            action();
        }
        finally
        {
            ImGui.PopStyleVar();
        }
    }

    /// <summary>
    ///     Executes an action with a temporary style variable change
    /// </summary>
    public static void WithStyleVar(ImGuiStyleVar styleVar, float value, Action action)
    {
        ImGui.PushStyleVar(styleVar, value);
        try
        {
            action();
        }
        finally
        {
            ImGui.PopStyleVar();
        }
    }

    /// <summary>
    ///     Displays a help marker with a tooltip
    /// </summary>
    public static void HelpMarker(string desc)
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
            ImGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    /// <summary>
    ///     Renders an input text field with optional validation
    /// </summary>
    public static bool ValidatedInputText(
        string label,
        ref string value,
        uint bufferSize,
        string? tooltip = null,
        Func<string, bool>? validator = null
    )
    {
        var changed = ImGui.InputText(label, ref value, bufferSize);

        // Show tooltip if provided
        if (tooltip != null && ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
            ImGui.TextUnformatted(tooltip);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        // Validate if changed and validator provided
        if (changed && validator != null)
        {
            var isValid = validator(value);
            if (!isValid)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "!");
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("Invalid input");
                    ImGui.EndTooltip();
                }
            }
        }

        return changed;
    }

    /// <summary>
    ///     Renders an input integer field with optional validation and constraints
    /// </summary>
    public static bool ValidatedInputInt(
        string label,
        ref int value,
        string? tooltip = null,
        Func<int, bool>? validator = null,
        int? min = null,
        int? max = null
    )
    {
        var changed = ImGui.InputInt(label, ref value);

        // Apply min/max constraints
        if (changed)
        {
            if (min.HasValue && value < min.Value)
            {
                value = min.Value;
            }

            if (max.HasValue && value > max.Value)
            {
                value = max.Value;
            }
        }

        // Show tooltip if provided
        if (tooltip != null && ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
            ImGui.TextUnformatted(tooltip);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        // Validate if changed and validator provided
        if (changed && validator != null)
        {
            var isValid = validator(value);
            if (!isValid)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "!");
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("Invalid input");
                    ImGui.EndTooltip();
                }
            }
        }

        return changed;
    }

    /// <summary>
    ///     Renders a float slider with tooltip and optional animation
    /// </summary>
    public static bool TooltipSliderFloat(
        string label,
        ref float value,
        float min,
        float max,
        string format = "%.3f",
        string? tooltip = null,
        bool animate = false
    )
    {
        if (animate)
        {
            // For animated sliders, use a pulsing accent color
            var time = (float)Math.Sin(ImGui.GetTime() * 2.0f) * 0.5f + 0.5f;
            var color = new Vector4(0.1f, 0.4f + time * 0.2f, 0.8f + time * 0.2f, 1.0f);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.SliderGrab, color);
        }

        var changed = ImGui.SliderFloat(label, ref value, min, max, format);

        if (animate)
        {
            ImGui.PopStyleColor(2);
        }

        if (tooltip != null && ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
            ImGui.TextUnformatted(tooltip);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        return changed;
    }

    /// <summary>
    ///     Renders a collapsible section header
    /// </summary>
    /// <summary>
    ///     Renders a collapsible section header
    /// </summary>
    public static bool SectionHeader(string label, bool defaultOpen = true)
    {
        // Define the style colors
        var styleColors = new List<(ImGuiCol, Vector4)>
        {
            (ImGuiCol.Header, new Vector4(0.15f, 0.45f, 0.8f, 0.8f)),
            (ImGuiCol.HeaderHovered, new Vector4(0.2f, 0.5f, 0.9f, 0.8f)),
            (ImGuiCol.HeaderActive, new Vector4(0.25f, 0.55f, 0.95f, 0.8f)),
        };

        var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        flags |= ImGuiTreeNodeFlags.Framed;
        flags |= ImGuiTreeNodeFlags.SpanAvailWidth;
        flags |= ImGuiTreeNodeFlags.AllowOverlap;
        flags |= ImGuiTreeNodeFlags.FramePadding;

        // Use the WithStyleColors method properly - pass the actual rendering logic in the action
        var opened = false;
        WithStyleColors(
            styleColors,
            () =>
            {
                WithStyleVar(
                    ImGuiStyleVar.FramePadding,
                    new Vector2(6, 6),
                    () =>
                    {
                        opened = ImGui.CollapsingHeader(label, flags);
                    }
                );
            }
        );

        return opened;
    }

    /// <summary>
    ///     Renders a button with animation effects
    /// </summary>
    public static bool AnimatedButton(string label, Vector2 size, bool isActive = false)
    {
        bool clicked;
        if (isActive)
        {
            // Pulse animation for active button
            var time = (float)Math.Sin(ImGui.GetTime() * 2.0f) * 0.5f + 0.5f;
            var color = new Vector4(0.1f, 0.5f + time * 0.3f, 0.9f, 0.7f + time * 0.3f);
            ImGui.PushStyleColor(ImGuiCol.Button, color);
            ImGui.PushStyleColor(
                ImGuiCol.ButtonHovered,
                new Vector4(color.X, color.Y, color.Z, 1.0f)
            );
            ImGui.PushStyleColor(
                ImGuiCol.ButtonActive,
                new Vector4(color.X * 1.1f, color.Y * 1.1f, color.Z * 1.1f, 1.0f)
            );
            clicked = ImGui.Button(label, size);
            ImGui.PopStyleColor(3);
        }
        else
        {
            // Default stylish button
            // ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.45f, 0.8f, 0.6f));
            // ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.5f, 0.9f, 0.7f));
            // ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.25f, 0.55f, 0.95f, 0.8f));
            clicked = ImGui.Button(label, size);
        }

        return clicked;
    }
}
