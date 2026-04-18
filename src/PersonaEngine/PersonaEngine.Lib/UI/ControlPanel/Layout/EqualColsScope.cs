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
    private readonly float _padding;
    private readonly ImGuiChildFlags _childFlags;
    private int _currentCol;
    private bool _childOpen;
    private bool _disposed;

    internal EqualColsScope(
        int count,
        float height,
        float gap,
        ImGuiChildFlags childFlags,
        float padding
    )
    {
        _count = count;
        _height = height;
        _gap = gap;
        _padding = padding;
        _childFlags = childFlags;
        _currentCol = -1;
        _childOpen = false;
        _disposed = false;

        // Use GetContentRegionAvail rather than LayoutContext.Width() so the
        // column calculation accounts for any active scrollbar that may have
        // reduced the available horizontal space (e.g., inside a scrollable FillChild).
        var parentWidth = ImGui.GetContentRegionAvail().X;
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

        var wp = new Vector2(_padding, _padding);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, wp);

        var visible = ImGui.BeginChild(
            $"##EqCol_{_currentCol}_{_height:F0}",
            new Vector2(_colWidth, _height),
            _childFlags
        );

        // Reset WP inside so nested children don't inherit column padding.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        _childOpen = true;
        var innerW = MathF.Max(0f, _colWidth - _padding * 2f);
        var innerH = MathF.Max(0f, _height - _padding * 2f);
        LayoutContext.Push(innerW, innerH);

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
