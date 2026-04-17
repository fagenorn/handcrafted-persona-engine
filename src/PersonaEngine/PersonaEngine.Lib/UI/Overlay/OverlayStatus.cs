namespace PersonaEngine.Lib.UI.Overlay;

/// <summary>
///     Real-world lifecycle state of the floating overlay. Distinct from
///     <see cref="Configuration.OverlayConfiguration.Enabled" /> (the *desired*
///     state) so the UI can show that work is in flight without lying about
///     the user's intent.
/// </summary>
public enum OverlayStatus
{
    /// <summary>No thread alive.</summary>
    Off,

    /// <summary>Thread spawned; window + D3D11 + DComp not yet past init.</summary>
    Starting,

    /// <summary>Thread is past init and pumping messages. "Good enough" live state.</summary>
    Active,

    /// <summary>WM_CLOSE posted; thread is winding down.</summary>
    Stopping,

    /// <summary>Last start attempt threw. <c>LastError</c> on the host explains why.</summary>
    Failed,
}
