using System.Runtime.InteropServices;

namespace PersonaEngine.Lib.UI.Native;

/// <summary>
///     Centralised Win32 P/Invoke declarations, constants, and structs used across
///     the UI subsystem. Keeps every <c>DllImport</c> in one file so callers
///     don't each carry their own copy of <c>GetCursorPos</c>, <c>POINT</c>, etc.
/// </summary>
internal static class Win32
{
    // ── Structs ────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

    // ── Constants: IDC_* cursor resource identifiers ───────────────────────────

    internal static readonly nint IDC_ARROW = 32512;
    internal static readonly nint IDC_SIZEALL = 32646;
    internal static readonly nint IDC_SIZENWSE = 32642;
    internal static readonly nint IDC_SIZENESW = 32643;

    // ── Constants: window styles ───────────────────────────────────────────────

    internal const uint WS_POPUP = 0x80000000;
    internal const uint WS_CLIPSIBLINGS = 0x04000000;
    internal const uint WS_CLIPCHILDREN = 0x02000000;

    // ── Constants: extended window styles ──────────────────────────────────────

    internal const uint WS_EX_TOOLWINDOW = 0x00000080;
    internal const uint WS_EX_TOPMOST = 0x00000008;
    internal const uint WS_EX_NOACTIVATE = 0x08000000;
    internal const uint WS_EX_TRANSPARENT = 0x00000020;
    internal const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    // ── Constants: ShowWindow command IDs ──────────────────────────────────────

    internal const int SW_HIDE = 0;
    internal const int SW_SHOWNOACTIVATE = 4;
    internal const int SW_MINIMIZE = 6;
    internal const int SW_MAXIMIZE = 3;
    internal const int SW_RESTORE = 9;

    // ── Constants: window messages ────────────────────────────────────────────

    internal const uint WM_DESTROY = 0x0002;
    internal const uint WM_SIZE = 0x0005;
    internal const uint WM_CLOSE = 0x0010;
    internal const uint WM_SETCURSOR = 0x0020;
    internal const uint WM_GETMINMAXINFO = 0x0024;
    internal const uint WM_NCHITTEST = 0x0084;
    internal const uint WM_NCCALCSIZE = 0x0083;
    internal const uint WM_NCLBUTTONDBLCLK = 0x00A3;
    internal const uint WM_NCRBUTTONUP = 0x00A5;
    internal const uint WM_SYSCOMMAND = 0x0112;
    internal const uint WM_CAPTURECHANGED = 0x0215;
    internal const uint WM_LBUTTONDOWN = 0x0201;
    internal const uint WM_LBUTTONUP = 0x0202;
    internal const uint WM_SIZING = 0x0214;
    internal const uint WM_APP = 0x8000;

    // ── Constants: WM_SIZING edge flags (wParam) ──────────────────────────────

    internal const int WMSZ_LEFT = 1;
    internal const int WMSZ_RIGHT = 2;
    internal const int WMSZ_TOP = 3;
    internal const int WMSZ_TOPLEFT = 4;
    internal const int WMSZ_TOPRIGHT = 5;
    internal const int WMSZ_BOTTOM = 6;
    internal const int WMSZ_BOTTOMLEFT = 7;
    internal const int WMSZ_BOTTOMRIGHT = 8;

    // ── Constants: non-client hit-test results ────────────────────────────────

    internal const int HTCLIENT = 1;
    internal const int HTCAPTION = 2;
    internal const int HTLEFT = 10;
    internal const int HTRIGHT = 11;
    internal const int HTTOP = 12;
    internal const int HTTOPLEFT = 13;
    internal const int HTTOPRIGHT = 14;
    internal const int HTBOTTOM = 15;
    internal const int HTBOTTOMLEFT = 16;
    internal const int HTBOTTOMRIGHT = 17;

    // ── Constants: window style bits ──────────────────────────────────────────

    internal const int GWL_STYLE = -16;
    internal const int GWL_EXSTYLE = -20;
    internal const int GWLP_WNDPROC = -4;
    internal const int WS_MAXIMIZE = 0x01000000;
    internal const int WS_THICKFRAME = 0x00040000;
    internal const int WS_CAPTION = 0x00C00000;

    // ── Constants: misc ───────────────────────────────────────────────────────

    internal const uint PM_REMOVE = 0x0001;
    internal const int MONITOR_DEFAULTTONEAREST = 2;
    internal const int TPM_RETURNCMD = 0x0100;

    // DWM rounded corners (Windows 11+).
    internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const int DWMWCP_ROUND = 2;

    // ── Constants: SetWindowPos flags ─────────────────────────────────────────

    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_NOSENDCHANGING = 0x0400;

    // ── Constants: SetWindowPos hWndInsertAfter sentinels ─────────────────────

    internal static readonly nint HWND_TOPMOST = -1;

    // ── Constants: SystemParametersInfo actions ──────────────────────────────

    internal const uint SPI_GETWORKAREA = 0x0030;

    // ── P/Invoke: kernel32 ────────────────────────────────────────────────────

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObjectEx(
        nint hHandle,
        uint dwMilliseconds,
        bool bAlertable
    );

    // ── P/Invoke: user32 ─────────────────────────────────────────────────────

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterClass(string lpClassName, nint hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam
    );

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    internal static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekMessage(
        out MSG lpMsg,
        nint hWnd,
        uint wMsgFilterMin,
        uint wMsgFilterMax,
        uint wRemoveMsg
    );

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    internal static extern nint DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern nint LoadCursor(nint hInstance, nint lpCursorName);

    [DllImport("user32.dll")]
    internal static extern nint SetCursor(nint hCursor);

    [DllImport("user32.dll")]
    internal static extern nint SetCapture(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    internal static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    internal static extern nint CallWindowProc(
        nint lpPrevWndFunc,
        nint hWnd,
        uint msg,
        nint wParam,
        nint lParam
    );

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags
    );

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    internal static extern nint GetSystemMenu(
        nint hWnd,
        [MarshalAs(UnmanagedType.Bool)] bool bRevert
    );

    [DllImport("user32.dll")]
    internal static extern int TrackPopupMenu(
        nint hMenu,
        int uFlags,
        int x,
        int y,
        int nReserved,
        nint hWnd,
        nint prcRect
    );

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        out RECT pvParam,
        uint fWinIni
    );

    // ── P/Invoke: dwmapi ─────────────────────────────────────────────────────

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(
        nint hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute
    );
}
