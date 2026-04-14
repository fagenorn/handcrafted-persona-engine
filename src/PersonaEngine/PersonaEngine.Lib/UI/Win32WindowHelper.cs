using System.Runtime.InteropServices;

namespace PersonaEngine.Lib.UI;

/// <summary>
///     Win32 interop for custom window chrome: edge resizing, title bar dragging,
///     maximize work-area constraints, and the system context menu.
/// </summary>
public sealed class Win32WindowHelper : IDisposable
{
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int WM_CLOSE = 0x0010;

    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    private const int SW_MAXIMIZE = 3;
    private const int SW_RESTORE = 9;
    private const int SW_MINIMIZE = 6;

    private const int GWLP_WNDPROC = -4;
    private const int GWL_STYLE = -16;
    private const int WS_MAXIMIZE = 0x01000000;

    private const int MONITOR_DEFAULTTONEAREST = 2;
    private const int TPM_RETURNCMD = 0x0100;

    private const int ResizeBorderWidth = 6;

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    private readonly nint _hwnd;
    private readonly nint _originalWndProc;
    private readonly WndProcDelegate _wndProcDelegate;

    // Hit-test regions in window-relative pixels.
    // Updated each frame by TitleBar after rendering.
    private float _titleBarHeight;
    private float _buttonsStartX;
    private float _buttonsEndX;

    public Win32WindowHelper(nint hwnd)
    {
        _hwnd = hwnd;
        _wndProcDelegate = WndProc;
        _originalWndProc = SetWindowLongPtr(
            _hwnd,
            GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_wndProcDelegate)
        );
    }

    public bool IsMaximized
    {
        get
        {
            var style = GetWindowLong(_hwnd, GWL_STYLE);
            return (style & WS_MAXIMIZE) != 0;
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

    public void Minimize() => ShowWindow(_hwnd, SW_MINIMIZE);

    public void ToggleMaximize() =>
        ShowWindow(_hwnd, IsMaximized ? SW_RESTORE : SW_MAXIMIZE);

    public void Close() => PostMessage(_hwnd, WM_CLOSE, 0, 0);

    public void ShowSystemMenu(int screenX, int screenY)
    {
        var menu = GetSystemMenu(_hwnd, false);
        var cmd = TrackPopupMenu(menu, TPM_RETURNCMD, screenX, screenY, 0, _hwnd, nint.Zero);
        if (cmd != 0)
            PostMessage(_hwnd, WM_SYSCOMMAND, cmd, 0);
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                return HandleNcHitTest(lParam);

            case WM_GETMINMAXINFO:
                HandleGetMinMaxInfo(lParam);
                return 0;
        }

        return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    private nint HandleNcHitTest(nint lParam)
    {
        var screenX = (short)(lParam.ToInt64() & 0xFFFF);
        var screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

        GetWindowRect(_hwnd, out var windowRect);

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

            if (onTop && onLeft) return HTTOPLEFT;
            if (onTop && onRight) return HTTOPRIGHT;
            if (onBottom && onLeft) return HTBOTTOMLEFT;
            if (onBottom && onRight) return HTBOTTOMRIGHT;
            if (onLeft) return HTLEFT;
            if (onRight) return HTRIGHT;
            if (onTop) return HTTOP;
            if (onBottom) return HTBOTTOM;
        }

        // Title bar region
        if (relY < _titleBarHeight)
        {
            // Window control buttons — let ImGui handle clicks
            if (relX >= _buttonsStartX && relX < _buttonsEndX)
                return HTCLIENT;

            // Drag zone — everything else in the title bar
            return HTCAPTION;
        }

        return HTCLIENT;
    }

    private void HandleGetMinMaxInfo(nint lParam)
    {
        var monitor = MonitorFromWindow(_hwnd, MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref monitorInfo);

        var work = monitorInfo.rcWork;
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        mmi.ptMaxPosition.X = work.Left - monitorInfo.rcMonitor.Left;
        mmi.ptMaxPosition.Y = work.Top - monitorInfo.rcMonitor.Top;
        mmi.ptMaxSize.X = work.Right - work.Left;
        mmi.ptMaxSize.Y = work.Bottom - work.Top;
        Marshal.StructureToPtr(mmi, lParam, false);
    }

    public void Dispose()
    {
        SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _originalWndProc);
    }

    // ── P/Invoke ────────────────────────────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern nint CallWindowProc(
        nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam
    );

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetSystemMenu(
        nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bRevert
    );

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(
        nint hMenu, int uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect
    );

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    // ── Native structs ──────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMaxTrackSize;
        public POINT ptMinTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
