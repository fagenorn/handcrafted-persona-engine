using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Auto-wrapping tile grid. Column count is computed each frame from the available width
///     and <paramref name="itemSize.X" /> + gap. Call <see cref="NextTile" /> once per tile;
///     the scope places it at the correct column/row and advances the cursor.
/// </summary>
public ref struct WrapGridScope
{
    private readonly Vector2 _itemSize;
    private readonly float _gap;
    private readonly int _columns;
    private readonly float _startX;

    private int _index;

    internal WrapGridScope(string id, Vector2 itemSize, float gap)
    {
        _itemSize = itemSize;
        _gap = gap;

        var avail = ImGui.GetContentRegionAvail().X;
        _columns = Math.Max(1, (int)((avail + gap) / (itemSize.X + gap)));
        _startX = ImGui.GetCursorPosX();
        _index = 0;

        ImGui.PushID(id);
    }

    /// <summary>
    ///     Positions the cursor at the next tile slot and returns the slot size.
    ///     Call <see cref="ImGui.BeginChild(string, Vector2, ImGuiChildFlags, ImGuiWindowFlags)" />
    ///     or <see cref="Ui.Card" /> with this size to render the tile.
    /// </summary>
    public Vector2 NextTile()
    {
        if (_index > 0)
        {
            var col = _index % _columns;
            if (col == 0)
            {
                // New row
                ImGui.SetCursorPosX(_startX);
            }
            else
            {
                ImGui.SameLine(0f, _gap);
            }
        }

        _index++;
        return _itemSize;
    }

    public void Dispose()
    {
        ImGui.PopID();
    }
}
