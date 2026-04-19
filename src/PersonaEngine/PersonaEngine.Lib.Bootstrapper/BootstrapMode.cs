namespace PersonaEngine.Lib.Bootstrapper;

public enum BootstrapMode
{
    /// <summary>Download only assets that are absent or hash-mismatched (default).</summary>
    AutoIfMissing,

    /// <summary>Force a full re-download regardless of local state.</summary>
    Reinstall,

    /// <summary>Hash-check every asset and report without downloading.</summary>
    Verify,

    /// <summary>Re-download only assets that fail their hash check.</summary>
    Repair,

    /// <summary>Skip all network activity; use whatever is already on disk.</summary>
    Offline,
}
