using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Horizontal band spanning the full parent width.
///     Height is determined by <see cref="Sz"/>: fixed pixels or remaining space.
/// </summary>
public ref struct RowScope
{
    private int _stylePushToken;
    private bool _disposed;

    internal RowScope(Sz height, Style style, ImGuiChildFlags childFlags)
    {
        _disposed = false;

        var parentWidth = LayoutContext.Width();
        float resolvedHeight;

        if (height.IsFixed)
        {
            resolvedHeight = height.Value;
        }
        else
        {
            resolvedHeight = LayoutContext.RemainingHeight();
        }

        resolvedHeight = MathF.Max(0f, resolvedHeight);
        LayoutContext.ConsumeHeight(resolvedHeight);

        _stylePushToken = style.Push();

        ImGui.BeginChild(
            $"##Row_{resolvedHeight:F0}_{ImGui.GetCursorPosY():F0}",
            new Vector2(parentWidth, resolvedHeight),
            childFlags
        );

        var innerWidth = MathF.Max(0f, parentWidth - style.Padding.X * 2f);
        var innerHeight = MathF.Max(0f, resolvedHeight - style.Padding.Y * 2f);
        LayoutContext.Push(innerWidth, innerHeight);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        LayoutContext.Pop();
        ImGui.EndChild();
        Style.Pop(_stylePushToken);
    }
}
