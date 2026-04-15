using Vortice.Direct3D11;
using Vortice.DXGI;

namespace PersonaEngine.Lib.UI.Overlay.Rendering;

/// <summary>
///     Draws the overlay's hover chrome (drag + resize buttons + a rounded
///     window outline) in D3D11. Premultiplied-over blending is used because
///     the swap chain expects premultiplied alpha — the pixel shaders output
///     that directly so this pass blends cleanly over the avatar drawn by
///     <see cref="QuadPipeline" />.
///
///     Coordinates four sub-pipelines:
///       * <see cref="ButtonPipeline" /> — one rounded-rect per draw, 6-vertex quad
///         in pixel space, local-offset attribute drives the SDF.
///       * <see cref="IconPipeline" /> — flat-color rectangles, batched dynamic vertex
///         buffer refreshed per frame.
///       * <see cref="OutlinePipeline" /> — a single fullscreen quad whose pixel shader
///         evaluates a rounded-rect SDF against the viewport to paint a thin accent
///         ring around the overlay whenever hover chrome is visible.
///       * <see cref="ArrowPipeline" /> — SDF double-headed arrow for the resize glyph.
/// </summary>
public sealed class ChromeRenderer : IDisposable
{
    private readonly ID3D11DeviceContext _context;

    private readonly ButtonPipeline _button;
    private readonly IconPipeline _icon;
    private readonly OutlinePipeline _outline;
    private readonly ArrowPipeline _arrow;

    private ID3D11BlendState? _premultipliedBlend;
    private ID3D11RasterizerState? _rasterizer;

    public ChromeRenderer(ID3D11Device device, ID3D11DeviceContext context)
    {
        _context = context;

        _button = new ButtonPipeline(device, context);
        _icon = new IconPipeline(device, context);
        _outline = new OutlinePipeline(device, context);
        _arrow = new ArrowPipeline(device, context);

        _button.Build();
        _icon.Build();
        _outline.Build();
        _arrow.Build();

        BuildBlendState(device);
        BuildRasterizer(device);
    }

    public void Render(int windowWidth, int windowHeight, float alpha, OverlayHandle hot)
    {
        if (alpha <= 0f)
        {
            return;
        }

        _context.OMSetBlendState(_premultipliedBlend);
        _context.RSSetState(_rasterizer);
        _context.RSSetViewport(0, 0, windowWidth, windowHeight);

        // Outline first so the buttons render on top of its corners cleanly.
        _outline.Draw(windowWidth, windowHeight, alpha);

        var (dragX, dragY) = OverlayChromeLayout.DragButtonPosition(windowWidth);
        var dragHot = hot == OverlayHandle.Drag;
        _button.Draw(windowWidth, windowHeight, dragX, dragY, alpha, dragHot);

        var (resizeX, resizeY) = OverlayChromeLayout.ResizeButtonPosition(
            windowWidth,
            windowHeight
        );
        var resizeHot = hot == OverlayHandle.Resize;
        _button.Draw(windowWidth, windowHeight, resizeX, resizeY, alpha, resizeHot);

        // Drag icon: rect-based four-way cross arrow. Cardinal-axis-aligned
        // so pixel-grid rendering produces clean edges — no SDF needed.
        _icon.Clear();
        BuildDragIcon(dragX, dragY);
        _icon.Flush(windowWidth, windowHeight, alpha, dragHot);

        // Resize icon: SDF-based double-headed diagonal arrow evaluated in
        // continuous shader space. A pixel-art rectangle approximation at
        // 45° on a 30-px button is inherently jagged; the SDF renders a
        // mathematically perfect arrow with one-pixel antialiasing.
        _arrow.Draw(windowWidth, windowHeight, resizeX, resizeY, alpha, resizeHot);
    }

    public void Dispose()
    {
        _button.Dispose();
        _icon.Dispose();
        _outline.Dispose();
        _arrow.Dispose();

        _premultipliedBlend?.Dispose();
        _rasterizer?.Dispose();
    }

    private void BuildDragIcon(int buttonX, int buttonY)
    {
        // Four-way cross arrow — the universal "move" glyph (same shape as
        // the IDC_SIZEALL cursor we swap to during drag). Two 2-px thin bars
        // forming a +, with a small triangular arrowhead at each end that
        // flares out to 6 px wide before tapering to the tip.
        // Total: 2 bars + 4 × 3 tapered columns/rows = 14 rects.
        var left = buttonX;
        var top = buttonY;

        // Central cross bars, centered on the button.
        _icon.PushRect(left + 9, top + 14, 12, 2); // horizontal, y = [14, 15]
        _icon.PushRect(left + 14, top + 9, 2, 12); // vertical, x = [14, 15]

        // Right arrowhead (tip at x=+23) — 3 vertical strips tapering toward tip.
        _icon.PushRect(left + 21, top + 12, 1, 6);
        _icon.PushRect(left + 22, top + 13, 1, 4);
        _icon.PushRect(left + 23, top + 14, 1, 2);

        // Left arrowhead (tip at x=+6).
        _icon.PushRect(left + 8, top + 12, 1, 6);
        _icon.PushRect(left + 7, top + 13, 1, 4);
        _icon.PushRect(left + 6, top + 14, 1, 2);

        // Top arrowhead (tip at y=+6) — 3 horizontal strips tapering toward tip.
        _icon.PushRect(left + 12, top + 8, 6, 1);
        _icon.PushRect(left + 13, top + 7, 4, 1);
        _icon.PushRect(left + 14, top + 6, 2, 1);

        // Bottom arrowhead (tip at y=+23).
        _icon.PushRect(left + 12, top + 21, 6, 1);
        _icon.PushRect(left + 13, top + 22, 4, 1);
        _icon.PushRect(left + 14, top + 23, 2, 1);
    }

    private void BuildBlendState(ID3D11Device device)
    {
        // Premultiplied-over: source is already premultiplied (our shaders output
        // RGB*A, A), so we add it into the destination scaled by (1 - src.a).
        var desc = new BlendDescription
        {
            AlphaToCoverageEnable = false,
            IndependentBlendEnable = false,
        };
        desc.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.One,
            DestinationBlend = Blend.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.InverseSourceAlpha,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };

        _premultipliedBlend = device.CreateBlendState(desc);
    }

    private void BuildRasterizer(ID3D11Device device)
    {
        // No culling: 2D overlay triangles can wind either way (the button quad's
        // two triangles flip winding, icon rects are whatever the push order
        // produced). CullNone avoids getting half-quads rendered.
        _rasterizer = device.CreateRasterizerState(RasterizerDescription.CullNone);
    }
}
