using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Renders N equal-width columns side by side with a specified gap.
///     Call <see cref="NextCol"/> to advance to each column.
/// </summary>
public ref struct EqualColsScope
{
    private readonly int _count;
    private readonly float _colWidth;
    private readonly float _height;
    private readonly float _gap;
    private readonly ImGuiChildFlags _childFlags;
    private int _currentCol;
    private bool _childOpen;
    private bool _disposed;

    internal EqualColsScope(int count, float height, float gap, ImGuiChildFlags childFlags)
    {
        _count = count;
        _height = height;
        _gap = gap;
        _childFlags = childFlags;
        _currentCol = -1;
        _childOpen = false;
        _disposed = false;

        var parentWidth = LayoutContext.Width();
        var totalGaps = (count - 1) * gap;
        _colWidth = MathF.Max(1f, (parentWidth - totalGaps) / count);
    }

    /// <summary>
    ///     Advances to the next column. Returns <see langword="true"/> if the column
    ///     is visible and content should be rendered; <see langword="false"/> if clipped.
    ///     Must be called exactly <c>count</c> times.
    /// </summary>
    public bool NextCol()
    {
        // Close the previous column's child if one is open
        if (_childOpen)
        {
            LayoutContext.Pop();
            ImGui.PopStyleVar(); // inner WP reset
            ImGui.EndChild();
            ImGui.PopStyleVar(); // outer WP(0,0)
            _childOpen = false;
        }

        _currentCol++;

        if (_currentCol >= _count)
            return false;

        if (_currentCol > 0)
            ImGui.SameLine(0f, _gap);

        // Explicitly set WP(0,0) so columns don't inherit parent's padding.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        var visible = ImGui.BeginChild(
            $"##EqCol_{_currentCol}_{_height:F0}",
            new Vector2(_colWidth, _height),
            _childFlags
        );

        // Reset WP inside too, so any nested children also get zero padding.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        _childOpen = true;
        LayoutContext.Push(_colWidth, _height);

        return visible;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_childOpen)
        {
            LayoutContext.Pop();
            ImGui.PopStyleVar(); // inner WP reset
            ImGui.EndChild();
            ImGui.PopStyleVar(); // outer WP(0,0)
        }
    }
}
