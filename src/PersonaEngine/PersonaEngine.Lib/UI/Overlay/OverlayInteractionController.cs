using System.Runtime.InteropServices;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace PersonaEngine.Lib.UI.Overlay;

/// <summary>
///     State machine for the floating overlay's user interaction:
///     - Polls the cursor every frame (because a click-through window receives no
///       mouse messages) to detect hover enter/leave and toggle click-through.
///     - When the overlay is not click-through, consumes Silk.NET mouse events to
///       drive drag-to-move (top-right button) and bottom-right-corner resize
///       (bottom-right button).
///     - Produces a fade alpha for <see cref="OverlayChromeRenderer" /> and emits
///       persisted position/size changes at the end of each gesture.
/// </summary>
public sealed class OverlayInteractionController : IDisposable
{
    private readonly IWindow _window;
    private readonly OverlayWin32Helper _win32;
    private readonly IInputContext _input;
    private readonly int _minWidth;
    private readonly int _minHeight;
    private readonly float _fadeDuration;
    private readonly bool _lockAspect;
    private readonly double _aspect;

    private IMouse? _mouse;
    private StandardCursor _lastCursor = StandardCursor.Default;

    private bool _isHovered;
    private bool _isDragging;
    private bool _isResizing;
    private OverlayHandle _activeHandle = OverlayHandle.None;

    // Last window bounds we pushed via Win32 so we can skip SetWindowPos when
    // the cursor hasn't actually moved the window. Redundant SetWindowPos calls
    // add WM_MOVE / WM_SIZE traffic that can stall the render thread.
    private int _lastPushedX;
    private int _lastPushedY;
    private int _lastPushedW;
    private int _lastPushedH;

    // Anchors captured at the start of a gesture, in screen coords.
    private POINT _gestureAnchorScreen;
    private Vector2D<int> _gestureAnchorWinPos;
    private Vector2D<int> _gestureAnchorWinSize;

    private float _chromeAlpha;

    public OverlayInteractionController(
        IWindow window,
        IInputContext input,
        OverlayWin32Helper win32,
        int minWidth,
        int minHeight,
        float fadeDuration,
        bool lockAspect,
        double aspect
    )
    {
        _window = window;
        _input = input;
        _win32 = win32;
        _minWidth = Math.Max(1, minWidth);
        _minHeight = Math.Max(1, minHeight);
        _fadeDuration = MathF.Max(0.01f, fadeDuration);
        _lockAspect = lockAspect;
        _aspect = aspect <= 0 ? 1 : aspect;

        AttachMouse();
        _input.ConnectionChanged += OnConnectionChanged;
    }

    public float ChromeAlpha => _chromeAlpha;

    public OverlayHandle ActiveHandle => _activeHandle;

    /// <summary>Fires when the user finishes moving the overlay.</summary>
    public event Action<Vector2D<int>>? PositionCommitted;

    /// <summary>Fires when the user finishes resizing the overlay.</summary>
    public event Action<Vector2D<int>>? SizeCommitted;

    public void Update(float deltaTime)
    {
        var cursorInside = _win32.IsCursorOverOverlay(out var relX, out var relY);

        // Classify cursor-under-handle first, both for chrome highlight and so a
        // mouse-down that arrives the same frame knows what was clicked.
        if (!_isDragging && !_isResizing)
        {
            _activeHandle = cursorInside
                ? OverlayChromeRenderer.HitTest(relX, relY, _window.Size.X, _window.Size.Y)
                : OverlayHandle.None;
        }

        // Chrome visibility: show whenever cursor is anywhere over the overlay rect.
        // Never release click-through mid-gesture — we must keep input captured
        // until MouseUp even if the cursor momentarily leaves the rect.
        var wantInteractive = cursorInside || _isDragging || _isResizing;
        if (_isHovered != wantInteractive)
        {
            _isHovered = wantInteractive;
            _win32.SetClickThrough(!wantInteractive);
        }

        UpdateCursorShape();

        var targetAlpha = wantInteractive ? 1f : 0f;
        var step = deltaTime / _fadeDuration;
        if (_chromeAlpha < targetAlpha)
        {
            _chromeAlpha = MathF.Min(targetAlpha, _chromeAlpha + step);
        }
        else if (_chromeAlpha > targetAlpha)
        {
            _chromeAlpha = MathF.Max(targetAlpha, _chromeAlpha - step);
        }

        // Drive drag / resize directly from screen cursor position — avoids jitter
        // from relying on Silk.NET MouseMove events that fire between frames.
        if (_isDragging)
        {
            ApplyDrag();
        }
        else if (_isResizing)
        {
            ApplyResize();
        }
    }

    private void UpdateCursorShape()
    {
        if (_mouse is null)
        {
            return;
        }

        StandardCursor desired;
        if (_isDragging)
        {
            desired = StandardCursor.ResizeAll;
        }
        else if (_isResizing)
        {
            desired = StandardCursor.NwseResize;
        }
        else
        {
            desired = _activeHandle switch
            {
                OverlayHandle.Drag => StandardCursor.ResizeAll,
                OverlayHandle.Resize => StandardCursor.NwseResize,
                _ => StandardCursor.Default,
            };
        }

        if (desired == _lastCursor)
        {
            return;
        }

        _mouse.Cursor.StandardCursor = desired;
        _lastCursor = desired;
    }

    private void ApplyDrag()
    {
        if (!GetCursorPos(out var pt))
        {
            return;
        }

        var newX = _gestureAnchorWinPos.X + (pt.X - _gestureAnchorScreen.X);
        var newY = _gestureAnchorWinPos.Y + (pt.Y - _gestureAnchorScreen.Y);

        if (newX == _lastPushedX && newY == _lastPushedY)
        {
            return;
        }

        // Direct Win32 move — bypasses Silk.NET's Position setter which does extra
        // property-change plumbing per call.
        _win32.MoveTo(newX, newY);
        _lastPushedX = newX;
        _lastPushedY = newY;
    }

    private void ApplyResize()
    {
        if (!GetCursorPos(out var pt))
        {
            return;
        }

        // Single bottom-right resize handle: the top-left corner stays pinned,
        // the width and height grow with the cursor delta.
        var dx = pt.X - _gestureAnchorScreen.X;
        var dy = pt.Y - _gestureAnchorScreen.Y;

        var newWidth = Math.Max(_minWidth, _gestureAnchorWinSize.X + dx);
        var newHeight = Math.Max(_minHeight, _gestureAnchorWinSize.Y + dy);

        if (_lockAspect)
        {
            // Prefer whichever axis produced the larger proportional delta so the
            // gesture feels responsive in both directions. The other axis is derived.
            var widthRatio = (double)newWidth / _gestureAnchorWinSize.X;
            var heightRatio = (double)newHeight / _gestureAnchorWinSize.Y;
            if (Math.Abs(widthRatio - 1.0) >= Math.Abs(heightRatio - 1.0))
            {
                newHeight = Math.Max(_minHeight, (int)Math.Round(newWidth / _aspect));
            }
            else
            {
                newWidth = Math.Max(_minWidth, (int)Math.Round(newHeight * _aspect));
            }
        }

        if (newWidth == _lastPushedW && newHeight == _lastPushedH)
        {
            return;
        }

        _win32.ResizeTo(newWidth, newHeight);
        _lastPushedW = newWidth;
        _lastPushedH = newHeight;
    }

    private void AttachMouse()
    {
        if (_mouse is not null)
        {
            return;
        }

        var mouse = _input.Mice.FirstOrDefault();
        if (mouse is null)
        {
            return;
        }

        _mouse = mouse;
        _mouse.MouseDown += OnMouseDown;
        _mouse.MouseUp += OnMouseUp;
    }

    private void DetachMouse()
    {
        if (_mouse is null)
        {
            return;
        }

        _mouse.MouseDown -= OnMouseDown;
        _mouse.MouseUp -= OnMouseUp;
        _mouse = null;
    }

    private void OnConnectionChanged(IInputDevice device, bool connected)
    {
        if (device is IMouse)
        {
            if (connected)
            {
                AttachMouse();
            }
            else
            {
                DetachMouse();
            }
        }
    }

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (button != MouseButton.Left || _isDragging || _isResizing)
        {
            return;
        }

        if (_activeHandle is OverlayHandle.None)
        {
            return;
        }

        if (!GetCursorPos(out var pt))
        {
            return;
        }

        _gestureAnchorScreen = pt;
        // Read bounds straight from Win32 so anchors reflect actual on-screen state,
        // not Silk.NET's cached Position/Size which may lag behind a previous gesture.
        _win32.GetBounds(out var x, out var y, out var w, out var h);
        _gestureAnchorWinPos = new Vector2D<int>(x, y);
        _gestureAnchorWinSize = new Vector2D<int>(w, h);
        _lastPushedX = x;
        _lastPushedY = y;
        _lastPushedW = w;
        _lastPushedH = h;

        switch (_activeHandle)
        {
            case OverlayHandle.Drag:
                _isDragging = true;
                break;
            case OverlayHandle.Resize:
                _isResizing = true;
                break;
        }
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return;
        }

        if (_isDragging || _isResizing)
        {
            // Read final bounds straight from Win32 — direct SetWindowPos calls
            // during the gesture bypassed Silk.NET, so its cached Position/Size
            // are stale until it next polls them.
            _win32.GetBounds(out var x, out var y, out var w, out var h);
            var pos = new Vector2D<int>(x, y);
            var size = new Vector2D<int>(w, h);

            if (_isDragging)
            {
                _isDragging = false;
                PositionCommitted?.Invoke(pos);
            }
            else
            {
                _isResizing = false;
                SizeCommitted?.Invoke(size);
                PositionCommitted?.Invoke(pos);
            }
        }
    }

    public void Dispose()
    {
        _input.ConnectionChanged -= OnConnectionChanged;
        DetachMouse();
    }

    // ── P/Invoke ────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
