using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace PersonaEngine.Lib.UI.Overlay;

/// <summary>
///     Owns the GDI-side plumbing for a <c>WS_EX_LAYERED</c> window rendered with
///     per-pixel alpha: a memory DC holding a bottom-up 32-bpp DIB whose pixel buffer
///     we can write directly (from <c>glReadPixels</c>) and then blit to the window
///     via <c>UpdateLayeredWindow</c>. Sidesteps the DWM+OpenGL per-pixel alpha path
///     which is unreliable on modern Windows + NVIDIA drivers — GDI layered windows
///     have worked reliably since XP.
/// </summary>
public sealed class OverlayLayeredSurface : IDisposable
{
    private readonly nint _hwnd;
    private readonly nint _screenDc;
    private readonly ILogger<OverlayLayeredSurface> _logger;

    private nint _memDc;
    private nint _dib;
    private nint _oldBitmap;

    public OverlayLayeredSurface(nint hwnd, ILogger<OverlayLayeredSurface> logger)
    {
        _hwnd = hwnd;
        _logger = logger;
        _screenDc = GetDC(nint.Zero);
        _memDc = CreateCompatibleDC(_screenDc);
    }

    /// <summary>
    ///     Pixel buffer of the current DIB (32-bit BGRA, row-major, bottom-up).
    ///     Stride is always <c>Width * 4</c> — no row padding for 32-bit DIBs.
    ///     Valid after the first <see cref="Resize" /> call and until disposal.
    /// </summary>
    public nint Pixels { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>
    ///     Creates (or recreates) the DIB at the given size. Must be called at
    ///     least once before <see cref="Present" />. Resizing reallocates the
    ///     buffer so any stored pixel data is lost.
    /// </summary>
    public void Resize(int width, int height)
    {
        if (width == Width && height == Height && _dib != nint.Zero)
        {
            return;
        }

        DisposeBitmap();

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = height, // positive = bottom-up, matches GL's Y origin
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB (uncompressed)
            },
        };

        _dib = CreateDIBSection(
            _screenDc,
            ref bmi,
            0, // DIB_RGB_COLORS
            out var pixels,
            nint.Zero,
            0
        );
        if (_dib == nint.Zero)
        {
            throw new InvalidOperationException("CreateDIBSection failed for overlay surface.");
        }

        Pixels = pixels;
        Width = width;
        Height = height;

        _oldBitmap = SelectObject(_memDc, _dib);
    }

    /// <summary>
    ///     Presents the current DIB contents to the layered window. Assumes pixels
    ///     are already premultiplied alpha (our shader output is premultiplied).
    ///     The window's on-screen position is not changed — the caller controls
    ///     position via <see cref="OverlayWin32Helper.MoveTo" /> for drag.
    ///     Returns true if the call succeeded; on failure the Win32 error is
    ///     logged for diagnosis.
    /// </summary>
    public unsafe bool Present()
    {
        if (_dib == nint.Zero)
        {
            return false;
        }

        var size = new SIZE { cx = Width, cy = Height };
        var srcPt = new POINT { x = 0, y = 0 };
        var blend = new BLENDFUNCTION
        {
            BlendOp = 0, // AC_SRC_OVER
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1, // AC_SRC_ALPHA — premultiplied per-pixel alpha
        };

        var ok = UpdateLayeredWindow(
            _hwnd,
            _screenDc,
            null,
            &size,
            _memDc,
            &srcPt,
            0,
            &blend,
            ULW_ALPHA
        );

        if (!ok)
        {
            _logger.LogWarning(
                "UpdateLayeredWindow failed with Win32 error {Error}",
                Marshal.GetLastWin32Error()
            );
        }

        return ok;
    }

    public void Dispose()
    {
        DisposeBitmap();
        if (_memDc != nint.Zero)
        {
            DeleteDC(_memDc);
            _memDc = nint.Zero;
        }

        if (_screenDc != nint.Zero)
        {
            ReleaseDC(nint.Zero, _screenDc);
        }
    }

    private void DisposeBitmap()
    {
        if (_dib == nint.Zero)
        {
            return;
        }

        if (_oldBitmap != nint.Zero)
        {
            SelectObject(_memDc, _oldBitmap);
            _oldBitmap = nint.Zero;
        }

        DeleteObject(_dib);
        _dib = nint.Zero;
        Pixels = nint.Zero;
        Width = 0;
        Height = 0;
    }

    // ── P/Invoke ────────────────────────────────────────────────────────────────

    private const uint ULW_ALPHA = 0x02;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;

        // bmiColors is omitted: uncompressed 32-bit DIBs don't need a color table.
    }

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hDC);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint hDC);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hDC, nint hObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint hObject);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(
        nint hdc,
        ref BITMAPINFO pbmi,
        uint usage,
        out nint ppvBits,
        nint hSection,
        uint offset
    );

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool UpdateLayeredWindow(
        nint hwnd,
        nint hdcDst,
        POINT* pptDst,
        SIZE* psize,
        nint hdcSrc,
        POINT* pptSrc,
        uint crKey,
        BLENDFUNCTION* pblend,
        uint dwFlags
    );
}
