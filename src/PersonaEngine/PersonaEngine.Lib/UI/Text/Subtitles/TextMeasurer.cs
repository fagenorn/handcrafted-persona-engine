using System.Collections.Concurrent;
using System.Numerics;

using FontStashSharp;

namespace PersonaEngine.Lib.UI.Text.Subtitles;

/// <summary>
///     Measures text dimensions using a specific font and caches the results.
///     Provides information about available rendering width and line height.
///     Thread-safe for use in concurrent processing.
/// </summary>
public class TextMeasurer
{
    private readonly ConcurrentDictionary<string, Vector2> _cache = new();

    private readonly float _sideMargin;

    private int _viewportHeight;

    private int _viewportWidth;

    public TextMeasurer(DynamicSpriteFont font, float sideMargin, int initialWidth, int initialHeight)
    {
        Font        = font;
        _sideMargin = sideMargin;
        UpdateViewport(initialWidth, initialHeight);
        LineHeight = Font.MeasureString("Ay").Y;
        if ( LineHeight <= 0 )
        {
            LineHeight = Font.FontSize;
        }
    }

    public float AvailableWidth { get; private set; }

    public float LineHeight { get; private set; }

    public DynamicSpriteFont Font { get; }

    public void UpdateViewport(int width, int height)
    {
        _viewportWidth  = width;
        _viewportHeight = height;
        AvailableWidth  = Math.Max(1, _viewportWidth - 2 * _sideMargin);
        _cache.Clear();
    }

    public Vector2 MeasureText(string text)
    {
        if ( string.IsNullOrEmpty(text) )
        {
            return Vector2.Zero;
        }

        return _cache.GetOrAdd(text, t => Font.MeasureString(t));
    }
}