using System.Numerics;

using FontStashSharp;

namespace PersonaEngine.Lib.UI.Text.Subtitles;

public record TextLayoutCache
{
    private readonly Dictionary<string, Vector2> _cache = new();

    private readonly DynamicSpriteFont _font;

    private readonly Lock _lock = new();

    private readonly float _sideMargin;

    private int _viewportWidth,
                _viewportHeight;

    public TextLayoutCache(DynamicSpriteFont font, float sideMargin, int initialWidth, int initialHeight)
    {
        _font       = font;
        _sideMargin = sideMargin;
        UpdateViewport(initialWidth, initialHeight);
        LineHeight = _font.MeasureString("Ay").Y;
    }

    public float AvailableWidth { get; private set; }

    public float LineHeight { get; private set; }

    public void UpdateViewport(int width, int height)
    {
        _viewportWidth  = width;
        _viewportHeight = height;
        AvailableWidth  = _viewportWidth - 2 * _sideMargin;
    }

    public Vector2 MeasureText(string text)
    {
        lock (_lock)
        {
            if ( _cache.TryGetValue(text, out var size) )
            {
                return size;
            }

            size         = _font.MeasureString(text);
            _cache[text] = size;

            return size;
        }
    }
}