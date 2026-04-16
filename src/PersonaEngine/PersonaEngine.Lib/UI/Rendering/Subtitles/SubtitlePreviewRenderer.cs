using System.Numerics;
using FontStashSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.Common;
using PersonaEngine.Lib.UI.ControlPanel;
using PersonaEngine.Lib.UI.Rendering.Text;
using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.UI.Rendering.Subtitles;

/// <summary>
///     Domain service that bakes a single frame of preview subtitle text into an
///     <see cref="OffscreenBuffer"/> using the configured TTF via FontStashSharp —
///     the exact same pipeline as <see cref="SubtitleRenderer"/>.
/// </summary>
public sealed class SubtitlePreviewRenderer : IDisposable
{
    private const string Line1 = "Your voice appears";
    private const string Line2Prefix = "right ";
    private const string Line2Highlight = "here";
    private const string Line2Suffix = " while streaming";

    private readonly FontProvider _fontProvider;
    private readonly ILogger<SubtitlePreviewRenderer> _logger;

    private GL? _gl;
    private OffscreenBuffer? _fb;
    private TextRenderer? _textRenderer;
    private SubtitleOptions _opts;
    private DynamicSpriteFont? _font;
    private volatile bool _dirty = true;
    private IDisposable? _changeSub;
    private bool _disposed;
    private bool _fontResolveWarned;

    public SubtitlePreviewRenderer(
        FontProvider fontProvider,
        IOptionsMonitor<SubtitleOptions> monitor,
        ILogger<SubtitlePreviewRenderer> logger
    )
    {
        _fontProvider = fontProvider;
        _logger = logger;
        _opts = monitor.CurrentValue;
        _changeSub = monitor.OnChange(
            (updated, _) =>
            {
                _opts = updated;
                _dirty = true;
            }
        );
    }

    public uint TextureId =>
        _fb?.ColorTextureId
        ?? throw new InvalidOperationException(
            "SubtitlePreviewRenderer.Initialize must be called first."
        );

    public int Width => _fb?.Width ?? 0;

    public int Height => _fb?.Height ?? 0;

    /// <summary>
    ///     Initialises GL resources. Safe to call more than once — subsequent calls are no-ops.
    /// </summary>
    public void Initialize(GL gl)
    {
        if (_gl != null)
        {
            return;
        }

        _gl = gl;
        _fb = OffscreenBuffer.Create(gl, 1, 1);
        _textRenderer = new TextRenderer(gl);
        ResolveFont();
    }

    /// <summary>
    ///     Renders the preview text into the offscreen buffer at the given dimensions.
    ///     No-ops when nothing has changed since the last call.
    /// </summary>
    public void Bake(int width, int height)
    {
        if (_gl is null || _fb is null || _textRenderer is null)
        {
            throw new InvalidOperationException("Call Initialize first.");
        }

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        // Re-resolve font if dirty or stale.
        if (_dirty || _font is null || _font.FontSize != _opts.FontSize)
        {
            ResolveFont();
        }

        var sizeChanged = width != _fb.Width || height != _fb.Height;

        if (!sizeChanged && !_dirty)
        {
            return;
        }

        if (sizeChanged)
        {
            _fb.Resize(width, height);
            _textRenderer.OnViewportChanged(width, height);
        }

        _fb.Bind();
        try
        {
            _gl.ClearColor(0f, 0f, 0f, 0f);
            _gl.Clear(ClearBufferMask.ColorBufferBit);

            if (_font != null)
            {
                _textRenderer.Begin();
                DrawPreviewLines();
                _textRenderer.End();
            }
        }
        finally
        {
            _fb.Unbind();
        }

        _dirty = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _changeSub?.Dispose();
        _textRenderer?.Dispose();
        _fb?.Dispose();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void ResolveFont()
    {
        try
        {
            _font = _fontProvider.GetFontSystem(_opts.Font).GetFont(_opts.FontSize);
            _fontResolveWarned = false;
        }
        catch (Exception ex)
        {
            if (!_fontResolveWarned)
            {
                _logger.LogWarning(
                    ex,
                    "SubtitlePreviewRenderer: failed to load font '{Font}'; falling back to first available font.",
                    _opts.Font
                );
                _fontResolveWarned = true;
            }

            var available = _fontProvider.GetAvailableFonts();
            if (available.Count == 0)
            {
                // No fonts at all — leave _font as-is (cached or null).
                return;
            }

            try
            {
                _font = _fontProvider.GetFontSystem(available[0]).GetFont(_opts.FontSize);
            }
            catch (Exception fallbackEx)
            {
                _logger.LogWarning(
                    fallbackEx,
                    "SubtitlePreviewRenderer: fallback font '{Font}' also failed to load.",
                    available[0]
                );
            }
        }
    }

    private void DrawPreviewLines()
    {
        // _font is guaranteed non-null by Bake()'s caller guard.
        var font = _font!;

        Theme.TryParseHex(_opts.Color, out var textVec);
        Theme.TryParseHex(_opts.HighlightColor, out var highlightVec);

        var textColor = ToFSColor(textVec);
        var highlightColor = ToFSColor(highlightVec);

        var line1Size = font.MeasureString(Line1);
        var prefixSize = font.MeasureString(Line2Prefix);
        var highlightSize = font.MeasureString(Line2Highlight);
        var suffixSize = font.MeasureString(Line2Suffix);

        var line2Width = prefixSize.X + highlightSize.X + suffixSize.X;

        var lineHeight = font.MeasureString("Ay").Y;
        var lineGap = lineHeight * 1.4f;

        var topY = (_fb!.Height - lineGap * 2f) * 0.5f;

        var line1Pos = new Vector2((_fb.Width - line1Size.X) * 0.5f, topY);
        var line2Left = (_fb.Width - line2Width) * 0.5f;
        var line2Y = topY + lineGap;

        font.DrawText(
            _textRenderer,
            Line1,
            line1Pos,
            textColor,
            effect: FontSystemEffect.Stroked,
            effectAmount: _opts.StrokeThickness
        );

        font.DrawText(
            _textRenderer,
            Line2Prefix,
            new Vector2(line2Left, line2Y),
            textColor,
            effect: FontSystemEffect.Stroked,
            effectAmount: _opts.StrokeThickness
        );

        font.DrawText(
            _textRenderer,
            Line2Highlight,
            new Vector2(line2Left + prefixSize.X, line2Y),
            highlightColor,
            effect: FontSystemEffect.Stroked,
            effectAmount: _opts.StrokeThickness
        );

        font.DrawText(
            _textRenderer,
            Line2Suffix,
            new Vector2(line2Left + prefixSize.X + highlightSize.X, line2Y),
            textColor,
            effect: FontSystemEffect.Stroked,
            effectAmount: _opts.StrokeThickness
        );
    }

    private static FSColor ToFSColor(Vector4 v) =>
        new((byte)(v.X * 255f), (byte)(v.Y * 255f), (byte)(v.Z * 255f), (byte)(v.W * 255f));
}
