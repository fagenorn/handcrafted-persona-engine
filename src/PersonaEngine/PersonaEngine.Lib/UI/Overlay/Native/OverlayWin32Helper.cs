using PersonaEngine.Lib.UI.Native;

namespace PersonaEngine.Lib.UI.Overlay.Native;

/// <summary>
///     Runtime Win32 operations for the floating overlay window: click-through
///     toggle, direct move / resize, cursor-over-overlay hit test, and bounds
///     query. The persistent ex-styles (<c>WS_EX_TOPMOST</c>,
///     <c>WS_EX_TOOLWINDOW</c>, <c>WS_EX_NOACTIVATE</c>,
///     <c>WS_EX_NOREDIRECTIONBITMAP</c>) are applied at window-creation time
///     inside <see cref="OverlayNativeWindow" /> so this class no longer touches
///     them.
///
///     <c>WS_EX_TRANSPARENT</c> (click-through) is toggled here at runtime —
///     that style genuinely flips with hover state, unlike the creation-time
///     styles. The controller flips it off while the cursor is over the overlay
///     so chrome buttons can receive WM_LBUTTONDOWN.
/// </summary>
public sealed class OverlayWin32Helper
{
    private readonly nint _hwnd;

    private bool _clickThrough;

    public OverlayWin32Helper(nint hwnd, bool initialClickThrough)
    {
        _hwnd = hwnd;
        _clickThrough = initialClickThrough;
    }

    public bool ClickThrough => _clickThrough;

    /// <summary>
    ///     Toggles <c>WS_EX_TRANSPARENT</c>. When click-through is on, all mouse
    ///     events pass to windows beneath. When off, the overlay receives normal
    ///     input and can react to the hover chrome.
    /// </summary>
    public void SetClickThrough(bool enabled)
    {
        if (enabled == _clickThrough)
        {
            return;
        }

        var ex = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE).ToInt64();
        if (enabled)
        {
            ex |= Win32.WS_EX_TRANSPARENT;
        }
        else
        {
            ex &= ~(long)Win32.WS_EX_TRANSPARENT;
        }

        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, new nint(ex));
        _clickThrough = enabled;
    }

    /// <summary>
    ///     Checks whether the cursor is within the overlay window's rect in virtual
    ///     screen coordinates. Used while click-through is enabled — in that state
    ///     mouse events never fire for the overlay because Windows skips
    ///     hit-testing it entirely. Because the overlay is WS_EX_TOPMOST, a
    ///     rect-only check is a good proxy for "visually on the overlay".
    /// </summary>
    public bool IsCursorOverOverlay(out int cursorXInWindow, out int cursorYInWindow)
    {
        cursorXInWindow = 0;
        cursorYInWindow = 0;

        if (!Win32.GetCursorPos(out var pt))
        {
            return false;
        }

        Win32.GetWindowRect(_hwnd, out var rect);
        if (pt.X < rect.Left || pt.X >= rect.Right || pt.Y < rect.Top || pt.Y >= rect.Bottom)
        {
            return false;
        }

        cursorXInWindow = pt.X - rect.Left;
        cursorYInWindow = pt.Y - rect.Top;
        return true;
    }

    /// <summary>
    ///     Moves the overlay window directly via Win32. Skipping the extra
    ///     property-change plumbing + using SWP_NOSENDCHANGING makes drag
    ///     smoother — each gesture update is a single thin Win32 call.
    /// </summary>
    public void MoveTo(int x, int y)
    {
        Win32.SetWindowPos(
            _hwnd,
            nint.Zero,
            x,
            y,
            0,
            0,
            Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_NOSENDCHANGING
        );
    }

    /// <summary>
    ///     Resizes the overlay window directly via Win32 (pinned top-left).
    /// </summary>
    public void ResizeTo(int width, int height)
    {
        Win32.SetWindowPos(
            _hwnd,
            nint.Zero,
            0,
            0,
            width,
            height,
            Win32.SWP_NOMOVE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_NOSENDCHANGING
        );
    }

    /// <summary>
    ///     Moves and resizes in a single <c>SetWindowPos</c> call. Needed by
    ///     corner-resize gestures that anchor a corner other than top-left:
    ///     when the bottom-left is pinned, changing the height also requires
    ///     shifting the top-left Y so the bottom-left stays put. Doing both
    ///     in one call avoids a visible intermediate jump that would occur
    ///     between separate <see cref="MoveTo" /> and <see cref="ResizeTo" />
    ///     calls.
    /// </summary>
    public void MoveAndResizeTo(int x, int y, int width, int height)
    {
        Win32.SetWindowPos(
            _hwnd,
            nint.Zero,
            x,
            y,
            width,
            height,
            Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_NOSENDCHANGING
        );
    }

    /// <summary>
    ///     Reads the overlay window's current position and size directly from
    ///     Win32.
    /// </summary>
    public void GetBounds(out int x, out int y, out int width, out int height)
    {
        Win32.GetWindowRect(_hwnd, out var rect);
        x = rect.Left;
        y = rect.Top;
        width = rect.Right - rect.Left;
        height = rect.Bottom - rect.Top;
    }
}
