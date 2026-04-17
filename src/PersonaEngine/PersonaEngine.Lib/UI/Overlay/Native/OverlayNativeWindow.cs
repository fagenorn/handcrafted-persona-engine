using System.Runtime.InteropServices;
using PersonaEngine.Lib.UI.Native;

namespace PersonaEngine.Lib.UI.Overlay.Native;

/// <summary>
///     Raw Win32 borderless top-level window created with
///     <c>WS_EX_NOREDIRECTIONBITMAP</c> — a hard requirement for DirectComposition
///     per-pixel-alpha HWNDs, since that ex-style tells DWM to skip allocating a
///     redirection surface that would otherwise composite over the DCompo output.
///     <c>WS_EX_NOREDIRECTIONBITMAP</c> must be set at <c>CreateWindowEx</c> time;
///     it cannot be toggled post-creation. That's why Silk.NET.Windowing is not
///     used here — GLFW doesn't expose this style, and flipping it afterwards
///     does nothing.
///
///     The window owns its own <c>WNDPROC</c>, so mouse / close / size events are
///     translated directly into C# events without Silk.NET input plumbing. The
///     class registration is per-instance (each overlay uses its own GUID-suffixed
///     class name) so multiple overlays can coexist without class collision.
/// </summary>
public sealed class OverlayNativeWindow : IDisposable
{
    // Kept alive as a field — the C delegate pointer is held by the registered
    // window class, so the GC must not collect it until after UnregisterClass.
    private readonly Win32.WndProcDelegate _wndProc;
    private readonly string _className;
    private readonly nint _hInstance;

    private nint _hwnd;
    private bool _classRegistered;
    private bool _destroyed;
    private int _width;
    private int _height;
    private OverlayCursor _cursor = OverlayCursor.Default;

    // Set while we're ourselves calling ReleaseCapture so the synchronous
    // WM_CAPTURECHANGED that follows doesn't double-fire LeftButtonUp.
    private bool _releasingCaptureSelf;

    public OverlayNativeWindow(string title, int x, int y, int width, int height)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _hInstance = Win32.GetModuleHandle(null);
        _wndProc = WndProcImpl;

        // Class name must be unique per instance — if two overlays exist in the
        // same process, reusing a class name would conflict with the registered
        // WNDPROC delegate of an already-disposed instance.
        _className = $"PersonaEngineOverlay_{Guid.NewGuid():N}";

        RegisterClass();
        CreateWindow(title, x, y);
    }

    /// <summary>HWND — valid until <see cref="Dispose" /> or external close.</summary>
    public nint Handle => _hwnd;

    /// <summary>Current client width in pixels — updated on WM_SIZE.</summary>
    public int Width => _width;

    /// <summary>Current client height in pixels — updated on WM_SIZE.</summary>
    public int Height => _height;

    /// <summary>True once WM_DESTROY has been processed; caller should break its loop.</summary>
    public bool IsClosing => _destroyed;

    /// <summary>
    ///     Desired cursor for this window. The actual SetCursor happens inside
    ///     <c>WM_SETCURSOR</c> so Windows doesn't immediately revert it between
    ///     mouse messages.
    /// </summary>
    public OverlayCursor Cursor
    {
        get => _cursor;
        set => _cursor = value;
    }

    /// <summary>Fires on WM_LBUTTONDOWN. Coordinates are client-relative pixels.</summary>
    public event Action<int, int>? LeftButtonDown;

    /// <summary>Fires on WM_LBUTTONUP. Coordinates are client-relative pixels.</summary>
    public event Action<int, int>? LeftButtonUp;

    /// <summary>Fires on WM_SIZE after <see cref="Width" />/<see cref="Height" /> update.</summary>
    public event Action<int, int>? Resized;

    /// <summary>Fires on WM_CLOSE — listeners can skip calling <see cref="Close" /> to veto.</summary>
    public event Action? Closing;

    /// <summary>Shows the window without stealing focus. Safe to call once the first frame is ready.</summary>
    public void Show()
    {
        if (_hwnd == nint.Zero)
        {
            return;
        }

        Win32.ShowWindow(_hwnd, Win32.SW_SHOWNOACTIVATE);
    }

    /// <summary>Hides the window.</summary>
    public void Hide()
    {
        if (_hwnd == nint.Zero)
        {
            return;
        }

        Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
    }

    /// <summary>
    ///     Routes all subsequent mouse messages to this HWND regardless of cursor
    ///     position. Call on gesture start (drag / resize) so releasing outside
    ///     the overlay rect still delivers WM_LBUTTONUP to us; without it the
    ///     gesture gets "stuck" when the cursor leaves our bounds.
    /// </summary>
    public void CaptureMouse()
    {
        if (_hwnd != nint.Zero)
        {
            Win32.SetCapture(_hwnd);
        }
    }

    /// <summary>Releases mouse capture previously taken with <see cref="CaptureMouse" />.</summary>
    public void ReleaseMouse()
    {
        if (_hwnd == nint.Zero)
        {
            return;
        }

        // ReleaseCapture synchronously dispatches WM_CAPTURECHANGED back to
        // this WndProc. Guard the flag so we don't treat that as an
        // "unexpected capture loss" and double-fire LeftButtonUp.
        _releasingCaptureSelf = true;
        try
        {
            Win32.ReleaseCapture();
        }
        finally
        {
            _releasingCaptureSelf = false;
        }
    }

    /// <summary>
    ///     Drains queued messages for this thread. Runs the WndProc synchronously
    ///     for each dispatched message so events fire inline with the pump call.
    /// </summary>
    public void PumpMessages()
    {
        while (Win32.PeekMessage(out var msg, nint.Zero, 0, 0, Win32.PM_REMOVE))
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }
    }

    /// <summary>
    ///     Requests window destruction. Safe to call from any thread — posts
    ///     WM_CLOSE which is delivered on the creating thread's next pump.
    ///     DestroyWindow itself is documented as creating-thread-only, so we
    ///     must not call it cross-thread.
    /// </summary>
    public void Close()
    {
        if (_hwnd != nint.Zero && !_destroyed)
        {
            Win32.PostMessage(_hwnd, Win32.WM_CLOSE, nint.Zero, nint.Zero);
        }
    }

    // User-defined message for cross-thread "move to (x, y)" requests. Posted by
    // <see cref="PostMove" /> and handled in the WndProc via SetWindowPos.
    private const uint WM_APP_MOVE = Win32.WM_APP + 1;

    /// <summary>
    ///     Moves the window to the given virtual-screen coordinates. Safe to
    ///     call from any thread — posts a custom message so SetWindowPos runs
    ///     on the creating thread.
    /// </summary>
    public void PostMove(int x, int y)
    {
        if (_hwnd == nint.Zero || _destroyed)
        {
            return;
        }

        Win32.PostMessage(_hwnd, WM_APP_MOVE, nint.Zero, PackXY(x, y));
    }

    public void Dispose()
    {
        if (_hwnd != nint.Zero && !_destroyed)
        {
            Win32.DestroyWindow(_hwnd);
            _hwnd = nint.Zero;
        }

        if (_classRegistered)
        {
            Win32.UnregisterClass(_className, _hInstance);
            _classRegistered = false;
        }
    }

    private void RegisterClass()
    {
        var wndClass = new Win32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(),
            // CS_OWNDC isn't strictly required but avoids DWM edge cases on
            // composition swap chains where the default DC caching behaviour
            // interferes with first-frame presentation on some drivers.
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _hInstance,
            hCursor = Win32.LoadCursor(nint.Zero, Win32.IDC_ARROW),
            // No background brush — we own every pixel via the swap chain.
            hbrBackground = nint.Zero,
            lpszClassName = _className,
        };

        if (Win32.RegisterClassEx(ref wndClass) == 0)
        {
            throw new InvalidOperationException(
                $"Overlay RegisterClassEx failed: Win32 error {Marshal.GetLastWin32Error()}"
            );
        }

        _classRegistered = true;
    }

    private void CreateWindow(string title, int x, int y)
    {
        var exStyle =
            Win32.WS_EX_TOOLWINDOW
            | Win32.WS_EX_TOPMOST
            | Win32.WS_EX_NOACTIVATE
            | Win32.WS_EX_NOREDIRECTIONBITMAP
            | Win32.WS_EX_TRANSPARENT; // start click-through; interaction toggles off on hover

        var style = Win32.WS_POPUP | Win32.WS_CLIPSIBLINGS | Win32.WS_CLIPCHILDREN;

        _hwnd = Win32.CreateWindowEx(
            exStyle,
            _className,
            title,
            style,
            x,
            y,
            _width,
            _height,
            nint.Zero,
            nint.Zero,
            _hInstance,
            nint.Zero
        );

        if (_hwnd == nint.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            Win32.UnregisterClass(_className, _hInstance);
            _classRegistered = false;
            throw new InvalidOperationException(
                $"Overlay CreateWindowEx failed: Win32 error {err}"
            );
        }
    }

    private nint WndProcImpl(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case Win32.WM_SETCURSOR:
                // Low word of lParam is the hit-test result. Only set our cursor
                // when the mouse is over our client area — otherwise default to
                // system behaviour so resize-cursor regions near the edge still
                // let the desktop reassert its arrow.
                var hitTest = (ushort)(lParam.ToInt64() & 0xFFFF);
                if (hitTest == Win32.HTCLIENT)
                {
                    var resource = _cursor switch
                    {
                        OverlayCursor.SizeAll => Win32.IDC_SIZEALL,
                        OverlayCursor.SizeNwse => Win32.IDC_SIZENWSE,
                        OverlayCursor.SizeNesw => Win32.IDC_SIZENESW,
                        _ => Win32.IDC_ARROW,
                    };
                    Win32.SetCursor(Win32.LoadCursor(nint.Zero, resource));
                    return 1; // TRUE — we handled it
                }

                return Win32.DefWindowProc(hwnd, msg, wParam, lParam);

            case Win32.WM_LBUTTONDOWN:
            {
                var (lx, ly) = UnpackXY(lParam);
                LeftButtonDown?.Invoke(lx, ly);
                return nint.Zero;
            }

            case Win32.WM_LBUTTONUP:
            {
                var (lx, ly) = UnpackXY(lParam);
                LeftButtonUp?.Invoke(lx, ly);
                return nint.Zero;
            }

            case Win32.WM_CAPTURECHANGED:
                // Another window stole capture (e.g. a dialog popped up, or
                // the OS cancelled our capture). Treat as an involuntary end
                // of the current gesture so state machines don't stay stuck.
                // When WE call ReleaseCapture the same message arrives but
                // _releasingCaptureSelf is set — ignore that case to avoid a
                // second LeftButtonUp on top of the one we already dispatched.
                if (!_releasingCaptureSelf)
                {
                    LeftButtonUp?.Invoke(0, 0);
                }
                return nint.Zero;

            case Win32.WM_SIZE:
            {
                var (w, h) = UnpackXY(lParam);
                _width = Math.Max(1, w);
                _height = Math.Max(1, h);
                Resized?.Invoke(_width, _height);
                return nint.Zero;
            }

            case Win32.WM_CLOSE:
                Closing?.Invoke();
                Win32.DestroyWindow(hwnd);
                return nint.Zero;

            case Win32.WM_DESTROY:
                _destroyed = true;
                return nint.Zero;

            case WM_APP_MOVE:
            {
                // Cross-thread move request from PostMove. lParam packs the
                // new position; size / z-order / activation are preserved.
                var (mx, my) = UnpackXY(lParam);
                Win32.SetWindowPos(
                    hwnd,
                    nint.Zero,
                    mx,
                    my,
                    0,
                    0,
                    Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE
                );
                return nint.Zero;
            }
        }

        return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static nint PackXY(int x, int y) =>
        // LOWORD = x, HIWORD = y. Mask to 16 bits so negative values don't
        // stomp the high word (coordinates outside the primary monitor may be
        // negative on multi-monitor setups).
        (nint)(((y & 0xFFFF) << 16) | (x & 0xFFFF));

    private static (int X, int Y) UnpackXY(nint lParam)
    {
        var v = lParam.ToInt64();
        // LOWORD / HIWORD — signed short for coordinates that may be negative
        // (dragging above the virtual screen origin produces negatives).
        var x = (short)(v & 0xFFFF);
        var y = (short)((v >> 16) & 0xFFFF);
        return (x, y);
    }
}
