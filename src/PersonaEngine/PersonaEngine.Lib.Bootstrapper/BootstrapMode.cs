namespace PersonaEngine.Lib.Bootstrapper;

public enum BootstrapMode
{
    /// <summary>If lock missing or any required asset missing, run picker. Otherwise skip.</summary>
    AutoIfMissing,

    /// <summary>Force picker; treat all assets as needing fresh download.</summary>
    Reinstall,

    /// <summary>Re-hash everything in lock file; flag mismatches as Repair actions.</summary>
    Verify,

    /// <summary>Re-download anything Verify flagged.</summary>
    Repair,

    /// <summary>Don't touch network. Fail fast if anything is missing.</summary>
    Offline,
}
