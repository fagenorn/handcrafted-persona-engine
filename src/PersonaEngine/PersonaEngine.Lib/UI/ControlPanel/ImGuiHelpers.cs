using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
///     Shared zero-allocation rendering helpers for the control panel. The
///     widget implementations live in sibling <c>Widgets/</c> files as
///     <see langword="partial"/> contributions to this class; this file only
///     hosts the trivial style-scope helpers they share.
/// </summary>
public static partial class ImGuiHelpers
{
    /// <summary>
    ///     Sets the mouse cursor to <see cref="ImGuiMouseCursor.Hand"/> when the
    ///     previous item is hovered. Call immediately after any clickable widget.
    /// </summary>
    public static void HandCursorOnHover()
    {
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
    }

    /// <summary>
    ///     Component-wise linear interpolation between two RGBA colours. Internal
    ///     because the only consumer is <see cref="ToggleSwitch"/>; promote to
    ///     public if a second caller appears.
    /// </summary>
    internal static Vector4 LerpColor(Vector4 a, Vector4 b, float t) =>
        new(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t,
            a.W + (b.W - a.W) * t
        );
}
