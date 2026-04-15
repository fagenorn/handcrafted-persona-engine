using System.Runtime.InteropServices;
using PersonaEngine.Lib.UI.Native;

namespace PersonaEngine.Lib.UI.Host;

/// <summary>
///     Win32 interop for custom window chrome: edge resizing, title bar dragging,
///     maximize work-area constraints, and the system context menu.
/// </summary>
public sealed class Win32WindowHelper : IDisposable
{
    private const int ResizeBorderWidth = 6;

    private readonly nint _hwnd;
    private readonly nint _originalWndProc;
    private readonly Win32.WndProcDelegate _wndProcDelegate;
    private readonly int _minWidth;
    private readonly int _minHeight;

    // Hit-test regions in window-relative pixels.
    // Updated each frame by TitleBar after rendering.
    private float _titleBarHeight;
    private float _buttonsStartX;
    private float _buttonsEndX;

    public Win32WindowHelper(nint hwnd, int minWidth, int minHeight)
    {
        _hwnd = hwnd;
        _minWidth = minWidth;
        _minHeight = minHeight;
        _wndProcDelegate = WndProc;
        _originalWndProc = Win32.SetWindowLongPtr(
            _hwnd,
            Win32.GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_wndProcDelegate)
        );

        // Add WS_THICKFRAME (enables edge resize) and WS_CAPTION (enables double-click
        // maximize) to the borderless window. WM_NCCALCSIZE handler prevents these from
        // adding any visible non-client area.
        var style = Win32.GetWindowLong(_hwnd, Win32.GWL_STYLE);
        style |= Win32.WS_THICKFRAME | Win32.WS_CAPTION;
        Win32.SetWindowLong(_hwnd, Win32.GWL_STYLE, style);

        // Request native rounded corners on Windows 11. Silently ignored on Windows 10
        // where the attribute is not recognized.
        var cornerPref = Win32.DWMWCP_ROUND;
        _ = Win32.DwmSetWindowAttribute(
            _hwnd,
            Win32.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref cornerPref,
            sizeof(int)
        );
    }

    public bool IsMaximized
    {
        get
        {
            var style = Win32.GetWindowLong(_hwnd, Win32.GWL_STYLE);
            return (style & Win32.WS_MAXIMIZE) != 0;
        }
    }

    /// <summary>
    ///     Updates the hit-test regions each frame after rendering the title bar.
    /// </summary>
    /// <param name="titleBarHeight">Height of the title bar in pixels.</param>
    /// <param name="buttonsStartX">X where window control buttons begin (window-relative).</param>
    /// <param name="buttonsEndX">X where window control buttons end (window-relative).</param>
    public void UpdateTitleBarRegion(float titleBarHeight, float buttonsStartX, float buttonsEndX)
    {
        _titleBarHeight = titleBarHeight;
        _buttonsStartX = buttonsStartX;
        _buttonsEndX = buttonsEndX;
    }

    public void Minimize() => Win32.ShowWindow(_hwnd, Win32.SW_MINIMIZE);

    public void ToggleMaximize() =>
        Win32.ShowWindow(_hwnd, IsMaximized ? Win32.SW_RESTORE : Win32.SW_MAXIMIZE);

    public void Close() => Win32.PostMessage(_hwnd, Win32.WM_CLOSE, 0, 0);

    public void ShowSystemMenu(int screenX, int screenY)
    {
        var menu = Win32.GetSystemMenu(_hwnd, false);
        var cmd = Win32.TrackPopupMenu(
            menu,
            Win32.TPM_RETURNCMD,
            screenX,
            screenY,
            0,
            _hwnd,
            nint.Zero
        );
        if (cmd != 0)
            Win32.PostMessage(_hwnd, Win32.WM_SYSCOMMAND, cmd, 0);
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case Win32.WM_NCHITTEST:
                return HandleNcHitTest(lParam);

            case Win32.WM_NCCALCSIZE:
                // Return 0 to tell Windows the entire window is client area,
                // preventing WS_THICKFRAME/WS_CAPTION from adding visible chrome.
                if (wParam != 0)
                    return 0;
                break;

            case Win32.WM_NCLBUTTONDBLCLK:
                // Handle double-click on title bar ourselves because GLFW's
                // WndProc doesn't pass it to DefWindowProc for maximize/restore.
                if (wParam == Win32.HTCAPTION)
                {
                    ToggleMaximize();
                    return 0;
                }
                break;

            case Win32.WM_NCRBUTTONUP:
                // Right-click on title bar — show system context menu.
                // ImGui can't handle this because Win32 intercepts non-client clicks.
                if (wParam == Win32.HTCAPTION)
                {
                    var x = (short)(lParam.ToInt64() & 0xFFFF);
                    var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                    ShowSystemMenu(x, y);
                    return 0;
                }
                break;

            case Win32.WM_SIZING:
                HandleSizing((int)wParam, lParam);
                return 1;

            case Win32.WM_GETMINMAXINFO:
                HandleGetMinMaxInfo(lParam);
                return 0;
        }

        return Win32.CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    private nint HandleNcHitTest(nint lParam)
    {
        var screenX = (short)(lParam.ToInt64() & 0xFFFF);
        var screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

        Win32.GetWindowRect(_hwnd, out var windowRect);

        var relX = screenX - windowRect.Left;
        var relY = screenY - windowRect.Top;
        var width = windowRect.Right - windowRect.Left;
        var height = windowRect.Bottom - windowRect.Top;

        // Edge resize zones (disabled when maximized)
        if (!IsMaximized)
        {
            var onLeft = relX < ResizeBorderWidth;
            var onRight = relX >= width - ResizeBorderWidth;
            var onTop = relY < ResizeBorderWidth;
            var onBottom = relY >= height - ResizeBorderWidth;

            if (onTop && onLeft)
                return Win32.HTTOPLEFT;
            if (onTop && onRight)
                return Win32.HTTOPRIGHT;
            if (onBottom && onLeft)
                return Win32.HTBOTTOMLEFT;
            if (onBottom && onRight)
                return Win32.HTBOTTOMRIGHT;
            if (onLeft)
                return Win32.HTLEFT;
            if (onRight)
                return Win32.HTRIGHT;
            if (onTop)
                return Win32.HTTOP;
            if (onBottom)
                return Win32.HTBOTTOM;
        }

        // Title bar region
        if (relY < _titleBarHeight)
        {
            // Window control buttons — let ImGui handle clicks
            if (relX >= _buttonsStartX && relX < _buttonsEndX)
                return Win32.HTCLIENT;

            // Drag zone — everything else in the title bar
            return Win32.HTCAPTION;
        }

        return Win32.HTCLIENT;
    }

    private void HandleGetMinMaxInfo(nint lParam)
    {
        // Only set maximize constraints — min size is enforced by WM_SIZING.
        // MINMAXINFO layout (int offsets): [0,1] ptReserved  [2,3] ptMaxSize  [4,5] ptMaxPosition
        var monitor = Win32.MonitorFromWindow(_hwnd, (uint)Win32.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new Win32.MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFO>(),
        };
        Win32.GetMonitorInfo(monitor, ref monitorInfo);

        var work = monitorInfo.rcWork;

        // ptMaxSize (offset 8) — size when maximized (work area = excludes taskbar)
        Marshal.WriteInt32(lParam, 8, work.Right - work.Left);
        Marshal.WriteInt32(lParam, 12, work.Bottom - work.Top);

        // ptMaxPosition (offset 16) — top-left when maximized
        Marshal.WriteInt32(lParam, 16, work.Left - monitorInfo.rcMonitor.Left);
        Marshal.WriteInt32(lParam, 20, work.Top - monitorInfo.rcMonitor.Top);
    }

    private void HandleSizing(int edge, nint lParam)
    {
        // Enforce minimum window size during edge resize. Clamp the edge being
        // dragged — clamping the opposite edge would make the window move instead
        // of stopping the resize.
        // lParam points to a RECT: Left(0), Top(4), Right(8), Bottom(12).
        var left = Marshal.ReadInt32(lParam, 0);
        var top = Marshal.ReadInt32(lParam, 4);
        var right = Marshal.ReadInt32(lParam, 8);
        var bottom = Marshal.ReadInt32(lParam, 12);

        if (right - left < _minWidth)
        {
            var draggingLeft =
                edge is Win32.WMSZ_LEFT or Win32.WMSZ_TOPLEFT or Win32.WMSZ_BOTTOMLEFT;
            if (draggingLeft)
                Marshal.WriteInt32(lParam, 0, right - _minWidth); // clamp Left
            else
                Marshal.WriteInt32(lParam, 8, left + _minWidth); // clamp Right
        }

        if (bottom - top < _minHeight)
        {
            var draggingTop = edge is Win32.WMSZ_TOP or Win32.WMSZ_TOPLEFT or Win32.WMSZ_TOPRIGHT;
            if (draggingTop)
                Marshal.WriteInt32(lParam, 4, bottom - _minHeight); // clamp Top
            else
                Marshal.WriteInt32(lParam, 12, top + _minHeight); // clamp Bottom
        }
    }

    public void Dispose()
    {
        Win32.SetWindowLongPtr(_hwnd, Win32.GWLP_WNDPROC, _originalWndProc);
    }
}
