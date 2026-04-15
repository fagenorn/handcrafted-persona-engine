using System.Runtime.InteropServices;

namespace PersonaEngine.Lib.UI.Overlay;

/// <summary>
///     Win32 interop for the floating overlay window. The overlay is displayed via
///     <c>UpdateLayeredWindow</c> (see <see cref="OverlayLayeredSurface" />), so we
///     need <c>WS_EX_LAYERED</c> on the window. The other ex-styles keep the
///     overlay always-on-top, hidden from the taskbar, and unable to steal focus.
///
///     <c>WS_EX_TRANSPARENT</c> is toggled at runtime to implement click-through:
///     a layered window's hit-test zone follows its alpha channel by default
///     (alpha = 0 → click passes through), but adding <c>WS_EX_TRANSPARENT</c>
///     forces every pixel to pass-through regardless of alpha. We enable it when
///     the cursor isn't over the overlay so clicks on the desktop behind it work
///     normally even where the avatar is opaque.
/// </summary>
public sealed class OverlayWin32Helper
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private static readonly nint HWND_TOPMOST = new(-1);

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSENDCHANGING = 0x0400;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private readonly nint _hwnd;

    private bool _clickThrough;

    public OverlayWin32Helper(nint hwnd)
    {
        _hwnd = hwnd;

        // WS_EX_LAYERED is required for UpdateLayeredWindow. Tool window keeps
        // us out of the taskbar and Alt+Tab; NOACTIVATE prevents click-steal
        // of focus from whatever the user is working on behind the overlay.
        var ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new nint(ex));

        // SWP_FRAMECHANGED is required after SetWindowLongPtr changes ex-styles —
        // without it, the style change (particularly WS_EX_LAYERED) doesn't fully
        // apply until the next unrelated window event, which can cause the
        // overlay to display as solid before UpdateLayeredWindow takes effect.
        SetWindowPos(
            _hwnd,
            HWND_TOPMOST,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED
        );

        // Start click-through. The interaction controller toggles this on hover.
        SetClickThrough(true);
    }

    public bool ClickThrough => _clickThrough;

    /// <summary>
    ///     Toggles <c>WS_EX_TRANSPARENT</c>. When click-through is on, all mouse
    ///     events pass to windows beneath regardless of the overlay's alpha. When
    ///     off, the overlay receives normal input and can react to the hover chrome.
    /// </summary>
    public void SetClickThrough(bool enabled)
    {
        if (enabled == _clickThrough)
        {
            return;
        }

        var ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        if (enabled)
        {
            ex |= WS_EX_TRANSPARENT;
        }
        else
        {
            ex &= ~(long)WS_EX_TRANSPARENT;
        }

        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new nint(ex));
        _clickThrough = enabled;
    }

    /// <summary>
    ///     Checks whether the cursor is within the overlay window's rect in virtual
    ///     screen coordinates. Used while click-through is enabled — in that state
    ///     Silk.NET mouse events never fire for the overlay because Windows skips
    ///     hit-testing the overlay entirely. Because the overlay is WS_EX_TOPMOST,
    ///     a rect-only check is a good proxy for "visually on the overlay".
    /// </summary>
    public bool IsCursorOverOverlay(out int cursorXInWindow, out int cursorYInWindow)
    {
        cursorXInWindow = 0;
        cursorYInWindow = 0;

        if (!GetCursorPos(out var pt))
        {
            return false;
        }

        GetWindowRect(_hwnd, out var rect);
        if (pt.X < rect.Left || pt.X >= rect.Right || pt.Y < rect.Top || pt.Y >= rect.Bottom)
        {
            return false;
        }

        cursorXInWindow = pt.X - rect.Left;
        cursorYInWindow = pt.Y - rect.Top;
        return true;
    }

    /// <summary>
    ///     Moves the overlay window directly via Win32, bypassing Silk.NET's
    ///     Position setter. Skipping the extra property-change plumbing + using
    ///     SWP_NOSENDCHANGING makes drag smoother — each gesture update is a
    ///     single thin Win32 call.
    /// </summary>
    public void MoveTo(int x, int y)
    {
        SetWindowPos(
            _hwnd,
            nint.Zero,
            x,
            y,
            0,
            0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSENDCHANGING
        );
    }

    /// <summary>
    ///     Resizes the overlay window directly via Win32 (pinned top-left).
    /// </summary>
    public void ResizeTo(int width, int height)
    {
        SetWindowPos(
            _hwnd,
            nint.Zero,
            0,
            0,
            width,
            height,
            SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSENDCHANGING
        );
    }

    /// <summary>
    ///     Reads the overlay window's current position and size directly from
    ///     Win32. Used at the start of a gesture so anchors reflect actual
    ///     on-screen state.
    /// </summary>
    public void GetBounds(out int x, out int y, out int width, out int height)
    {
        GetWindowRect(_hwnd, out var rect);
        x = rect.Left;
        y = rect.Top;
        width = rect.Right - rect.Left;
        height = rect.Bottom - rect.Top;
    }

    // ── P/Invoke ────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
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
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);
}
