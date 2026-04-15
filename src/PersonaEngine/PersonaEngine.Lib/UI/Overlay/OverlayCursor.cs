namespace PersonaEngine.Lib.UI.Overlay;

/// <summary>
///     The subset of Win32 IDC_* cursors the overlay actually needs. Mapped to
///     real cursor handles inside <see cref="OverlayNativeWindow" /> on WM_SETCURSOR.
/// </summary>
public enum OverlayCursor
{
    Default,
    SizeAll,
    SizeNwse,
    SizeNesw,
}
