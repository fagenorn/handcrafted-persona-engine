using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Resizable horizontal split with a draggable divider.
///     Use <see cref="Left"/> and <see cref="Right"/> to render into each side.
/// </summary>
public ref struct SplitScope
{
    private static readonly Dictionary<string, float> _splitPositions = new();

    private const float DividerWidth = 6f;

    private readonly string _id;
    private readonly float _totalWidth;
    private readonly float _height;
    private readonly float _minLeft;
    private readonly float _minRight;
    private readonly Style _leftStyle;
    private readonly Style _rightStyle;
    private bool _disposed;

    internal SplitScope(
        string id,
        Sz height,
        float initialLeft,
        float minLeft,
        float minRight,
        Style leftStyle,
        Style rightStyle
    )
    {
        _id = id;
        _leftStyle = leftStyle;
        _rightStyle = rightStyle;
        _minLeft = minLeft;
        _minRight = minRight;
        _disposed = false;

        _totalWidth = LayoutContext.Width();

        if (height.IsFixed)
        {
            _height = height.Value;
        }
        else
        {
            _height = LayoutContext.RemainingHeight();
        }

        _height = MathF.Max(0f, _height);
        LayoutContext.ConsumeHeight(_height);

        if (!_splitPositions.ContainsKey(id))
            _splitPositions[id] = initialLeft;

        // Container child with zero padding/spacing (children tile inside)
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.BeginChild($"##{id}_container", new Vector2(_totalWidth, _height));
    }

    /// <summary>Returns a scope for the left side of the split.</summary>
    public SplitChildScope Left()
    {
        var splitPos = _splitPositions[_id];
        var leftWidth = MathF.Max(0f, splitPos);
        return new SplitChildScope($"##{_id}_left", leftWidth, _height, _leftStyle);
    }

    /// <summary>Renders the divider and returns a scope for the right side.</summary>
    public SplitChildScope Right()
    {
        var splitPos = _splitPositions[_id];

        // Divider — an invisible button the user can drag
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Surface1);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.SurfaceHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.SurfaceHover);
        ImGui.Button($"##{_id}_div", new Vector2(DividerWidth, _height));
        ImGui.PopStyleColor(3);

        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

        if (ImGui.IsItemActive())
        {
            var delta = ImGui.GetIO().MouseDelta.X;
            splitPos += delta;
            var maxLeft = _totalWidth - _minRight - DividerWidth;
            splitPos = MathF.Max(_minLeft, MathF.Min(maxLeft, splitPos));
            _splitPositions[_id] = splitPos;
        }

        ImGui.SameLine();

        var rightWidth = MathF.Max(0f, _totalWidth - splitPos - DividerWidth);
        return new SplitChildScope($"##{_id}_right", rightWidth, _height, _rightStyle);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        ImGui.EndChild();
        ImGui.PopStyleVar(2); // ItemSpacing, WindowPadding
    }
}

/// <summary>
///     One side of a <see cref="SplitScope"/>. Manages its own child window and style.
///     Uses two-phase push: container (padding, bg) before BeginChild,
///     content (item spacing) after BeginChild.
/// </summary>
public ref struct SplitChildScope
{
    private int _outerVarCount;
    private int _outerColorCount;
    private bool _disposed;

    internal SplitChildScope(string id, float width, float height, Style style)
    {
        _disposed = false;
        _outerVarCount = 0;
        _outerColorCount = 0;

        var padding = style.Padding;

        // ── Before BeginChild ───────────────────────────────────────────
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
        _outerVarCount++;

        if (style.ChildBg is { } bg)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, bg);
            _outerColorCount++;
        }

        ImGui.BeginChild(id, new Vector2(width, height));

        // ── After BeginChild ────────────────────────────────────────────
        // Set content spacing. Reset WP to zero so nested children
        // don't inherit this scope's padding.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, style.ItemSpacing);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        var innerWidth = MathF.Max(0f, width - padding.X * 2f);
        var innerHeight = MathF.Max(0f, height - padding.Y * 2f);
        LayoutContext.Push(innerWidth, innerHeight);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        LayoutContext.Pop();
        ImGui.PopStyleVar(2); // inner WP reset + inner IS
        ImGui.EndChild();
        if (_outerColorCount > 0)
            ImGui.PopStyleColor(_outerColorCount);
        ImGui.PopStyleVar(_outerVarCount);
    }
}
