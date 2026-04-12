using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Bundles padding, item spacing, and optional background color for a layout scope.
///     Scopes push these vars on open and pop on close — panels never touch PushStyleVar.
/// </summary>
public readonly struct Style
{
    public readonly Vector2 Padding;
    public readonly Vector2 ItemSpacing;
    public readonly Vector4? ChildBg;

    public Style(Vector2 padding, Vector2 itemSpacing, Vector4? childBg = null)
    {
        Padding = padding;
        ItemSpacing = itemSpacing;
        ChildBg = childBg;
    }

    /// <summary>Pushes style vars to ImGui. Returns a token encoding the push count.</summary>
    public int Push()
    {
        var varCount = 0;
        var colorCount = 0;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Padding);
        varCount++;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ItemSpacing);
        varCount++;

        if (ChildBg is { } bg)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, bg);
            colorCount++;
        }

        // Encode: colorCount * 1000 + varCount
        return colorCount * 1000 + varCount;
    }

    /// <summary>Pops the style vars/colors that were pushed by <see cref="Push"/>.</summary>
    public static void Pop(int pushToken)
    {
        var colorCount = pushToken / 1000;
        var varCount = pushToken % 1000;

        if (colorCount > 0)
            ImGui.PopStyleColor(colorCount);

        if (varCount > 0)
            ImGui.PopStyleVar(varCount);
    }
}
