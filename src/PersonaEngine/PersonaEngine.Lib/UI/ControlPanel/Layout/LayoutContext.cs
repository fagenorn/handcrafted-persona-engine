namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Tracks available space for nested layout scopes via a static stack.
///     Each scope pushes on open and pops on close.
/// </summary>
public static class LayoutContext
{
    private static readonly Stack<Entry> _stack = new();

    public static void Push(float width, float height)
    {
        _stack.Push(new Entry(width, height));
    }

    public static (float Width, float Height) Peek()
    {
        var entry = _stack.Peek();
        return (entry.Width, entry.Height);
    }

    public static void Pop() => _stack.Pop();

    /// <summary>
    ///     Subtracts <paramref name="pixels"/> from the current context's remaining height.
    ///     Used by Row scopes to track how much vertical space has been consumed.
    /// </summary>
    public static void ConsumeHeight(float pixels)
    {
        var entry = _stack.Peek();
        entry.ConsumedHeight += pixels;
    }

    /// <summary>Returns the remaining height after all consumed rows.</summary>
    public static float RemainingHeight()
    {
        var entry = _stack.Peek();
        return MathF.Max(0f, entry.Height - entry.ConsumedHeight);
    }

    /// <summary>Returns the full width of the current context.</summary>
    public static float Width() => _stack.Peek().Width;

    private class Entry
    {
        public readonly float Width;
        public readonly float Height;
        public float ConsumedHeight;

        public Entry(float width, float height)
        {
            Width = width;
            Height = height;
        }
    }
}
