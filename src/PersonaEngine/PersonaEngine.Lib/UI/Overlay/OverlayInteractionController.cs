using PersonaEngine.Lib.UI.Native;
using PersonaEngine.Lib.UI.Overlay.Native;

namespace PersonaEngine.Lib.UI.Overlay;

/// <summary>
///     State machine for the floating overlay's user interaction:
///     - Polls the cursor every frame (because a click-through window receives no
///       mouse messages) to detect hover enter/leave and toggle click-through.
///     - When the overlay is not click-through, consumes the native window's mouse
///       events to drive drag-to-move (top-right button) and SE-corner resize
///       (bottom-right button, pinning the top-left corner).
///     - Produces a fade alpha for the chrome renderer and emits persisted
///       position/size changes at the end of each gesture.
/// </summary>
public sealed class OverlayInteractionController : IDisposable
{
    // Fixed — exposing these as config knobs was control-panel clutter.
    // The overlay always locks aspect (it mirrors a Spout source whose aspect
    // is inherent) and the chrome fade duration was never tuned by users.
    private const float ChromeFadeDurationSeconds = 0.12f;

    private readonly OverlayNativeWindow _window;
    private readonly OverlayWin32Helper _win32;
    private readonly int _minWidth;
    private readonly int _minHeight;
    private readonly double _aspect;

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
    private Win32.POINT _gestureAnchorScreen;
    private (int X, int Y) _gestureAnchorWinPos;
    private (int X, int Y) _gestureAnchorWinSize;

    private float _chromeAlpha;

    public OverlayInteractionController(
        OverlayNativeWindow window,
        OverlayWin32Helper win32,
        int minWidth,
        int minHeight,
        double aspect
    )
    {
        _window = window;
        _win32 = win32;
        _minWidth = Math.Max(1, minWidth);
        _minHeight = Math.Max(1, minHeight);
        _aspect = aspect <= 0 ? 1 : aspect;

        _window.LeftButtonDown += OnLeftButtonDown;
        _window.LeftButtonUp += OnLeftButtonUp;
    }

    public float ChromeAlpha => _chromeAlpha;

    public OverlayHandle ActiveHandle => _activeHandle;

    /// <summary>Fires when the user finishes moving the overlay.</summary>
    public event Action<(int X, int Y)>? PositionCommitted;

    /// <summary>Fires when the user finishes resizing the overlay.</summary>
    public event Action<(int X, int Y)>? SizeCommitted;

    public void Update(float deltaTime)
    {
        var cursorInside = _win32.IsCursorOverOverlay(out var relX, out var relY);

        // Classify cursor-under-handle first, both for chrome highlight and so a
        // mouse-down that arrives the same frame knows what was clicked.
        if (!_isDragging && !_isResizing)
        {
            _activeHandle = cursorInside
                ? OverlayChromeLayout.HitTest(relX, relY, _window.Width, _window.Height)
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
        var step = deltaTime / ChromeFadeDurationSeconds;
        if (_chromeAlpha < targetAlpha)
        {
            _chromeAlpha = MathF.Min(targetAlpha, _chromeAlpha + step);
        }
        else if (_chromeAlpha > targetAlpha)
        {
            _chromeAlpha = MathF.Max(targetAlpha, _chromeAlpha - step);
        }

        // Drive drag / resize directly from screen cursor position — avoids jitter
        // from relying on mouse-move events that fire between frames.
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
        OverlayCursor desired;
        if (_isDragging)
        {
            desired = OverlayCursor.SizeAll;
        }
        else if (_isResizing)
        {
            desired = OverlayCursor.SizeNwse;
        }
        else
        {
            desired = _activeHandle switch
            {
                OverlayHandle.Drag => OverlayCursor.SizeAll,
                // NW-SE cursor — the resize button sits in the bottom-right
                // corner and dragging it stretches the bottom-right corner
                // (top-left pinned), i.e. movement along the NW↔SE axis.
                OverlayHandle.Resize => OverlayCursor.SizeNwse,
                _ => OverlayCursor.Default,
            };
        }

        if (_window.Cursor != desired)
        {
            _window.Cursor = desired;
        }
    }

    private void ApplyDrag()
    {
        if (!Win32.GetCursorPos(out var pt))
        {
            return;
        }

        var newX = _gestureAnchorWinPos.X + (pt.X - _gestureAnchorScreen.X);
        var newY = _gestureAnchorWinPos.Y + (pt.Y - _gestureAnchorScreen.Y);

        if (newX == _lastPushedX && newY == _lastPushedY)
        {
            return;
        }

        // Direct Win32 move — bypasses extra property-change plumbing per call.
        _win32.MoveTo(newX, newY);
        _lastPushedX = newX;
        _lastPushedY = newY;
    }

    private void ApplyResize()
    {
        if (!Win32.GetCursorPos(out var pt))
        {
            return;
        }

        // Bottom-right resize handle: the top-left corner stays pinned while
        // the bottom-right corner follows the cursor. Dragging right grows the
        // width; dragging DOWN grows the height. Width and height always
        // scale together so the aspect ratio stays locked.
        var dx = pt.X - _gestureAnchorScreen.X;
        var dy = pt.Y - _gestureAnchorScreen.Y;

        var scaleX = 1.0 + (double)dx / _gestureAnchorWinSize.X;
        var scaleY = 1.0 + (double)dy / _gestureAnchorWinSize.Y;

        // Take the larger scale so dragging freely in either axis grows the
        // window. Clamp as a scalar (not per-dimension) so aspect stays locked
        // even when the gesture would drive one dimension below its minimum.
        var scale = Math.Max(scaleX, scaleY);
        var minScale = Math.Max(
            (double)_minWidth / _gestureAnchorWinSize.X,
            (double)_minHeight / _gestureAnchorWinSize.Y
        );
        if (scale < minScale)
        {
            scale = minScale;
        }

        var newWidth = (int)Math.Round(_gestureAnchorWinSize.X * scale);
        var newHeight = (int)Math.Round(_gestureAnchorWinSize.Y * scale);

        // Keep the top-left pixel fixed — neither X nor Y moves for this gesture.
        var newX = _gestureAnchorWinPos.X;
        var newY = _gestureAnchorWinPos.Y;

        if (
            newX == _lastPushedX
            && newY == _lastPushedY
            && newWidth == _lastPushedW
            && newHeight == _lastPushedH
        )
        {
            return;
        }

        _win32.MoveAndResizeTo(newX, newY, newWidth, newHeight);
        _lastPushedX = newX;
        _lastPushedY = newY;
        _lastPushedW = newWidth;
        _lastPushedH = newHeight;
    }

    private void OnLeftButtonDown(int _, int __)
    {
        if (_isDragging || _isResizing)
        {
            return;
        }

        if (_activeHandle is OverlayHandle.None)
        {
            return;
        }

        if (!Win32.GetCursorPos(out var pt))
        {
            return;
        }

        _gestureAnchorScreen = pt;
        // Read bounds straight from Win32 so anchors reflect actual on-screen state,
        // not a cached size which may lag behind a previous gesture.
        _win32.GetBounds(out var x, out var y, out var w, out var h);
        _gestureAnchorWinPos = (x, y);
        _gestureAnchorWinSize = (w, h);
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

        // Capture so the MouseUp reaches us even if the cursor has left the
        // overlay rect — common during resize when the user pulls outward.
        // Without capture, Windows routes subsequent mouse messages to
        // whatever window is under the cursor and the gesture gets stuck
        // until the user clicks us again.
        _window.CaptureMouse();
    }

    private void OnLeftButtonUp(int _, int __)
    {
        if (!_isDragging && !_isResizing)
        {
            return;
        }

        // Read final bounds straight from Win32 — direct SetWindowPos calls
        // during the gesture bypassed any cached size fields.
        _win32.GetBounds(out var x, out var y, out var w, out var h);
        var pos = (x, y);
        var size = (w, h);

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

        _window.ReleaseMouse();
    }

    public void Dispose()
    {
        _window.LeftButtonDown -= OnLeftButtonDown;
        _window.LeftButtonUp -= OnLeftButtonUp;
    }
}
