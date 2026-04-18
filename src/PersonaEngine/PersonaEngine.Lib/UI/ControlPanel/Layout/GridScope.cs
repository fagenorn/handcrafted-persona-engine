using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Auto-height equal-width column grid backed by an ImGui table.
///     Unlike <see cref="EqualColsScope"/>, rows auto-size to their content —
///     no fixed height, no child windows, no scrollbars.
/// </summary>
public ref struct GridScope
{
    /// <summary>
    ///     Pre-computed column ids to avoid per-frame <c>$"##c{i}"</c> string interpolation.
    ///     Covers the common case (up to 8 columns); anything larger falls back to interpolation.
    /// </summary>
    private static readonly string[] ColIds =
    {
        "##c0",
        "##c1",
        "##c2",
        "##c3",
        "##c4",
        "##c5",
        "##c6",
        "##c7",
    };

    private readonly bool _open;
    private bool _disposed;

    internal GridScope(string id, int columns)
    {
        _disposed = false;
        _open = ImGui.BeginTable(id, columns, ImGuiTableFlags.SizingStretchSame);

        if (!_open)
            return;

        for (var i = 0; i < columns; i++)
        {
            var colId = i < ColIds.Length ? ColIds[i] : $"##c{i}";
            ImGui.TableSetupColumn(colId, ImGuiTableColumnFlags.WidthStretch);
        }
    }

    /// <summary>Begins a new row. Call before the first <see cref="Col"/> in each row.</summary>
    public void Row()
    {
        if (_open)
            ImGui.TableNextRow();
    }

    /// <summary>Advances to the next column within the current row.</summary>
    public void Col()
    {
        if (_open)
            ImGui.TableNextColumn();
    }

    /// <summary>
    ///     Advances to the next column and makes the next widget fill its width.
    ///     Shorthand for <see cref="Col"/> followed by <c>SetNextItemWidth(-1)</c>.
    /// </summary>
    public void ColFill()
    {
        if (!_open)
            return;

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1f);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_open)
            ImGui.EndTable();
    }
}
