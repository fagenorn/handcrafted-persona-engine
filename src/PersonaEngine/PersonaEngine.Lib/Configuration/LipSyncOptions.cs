namespace PersonaEngine.Lib.Configuration;

/// <summary>
///     Lip-sync engine backing the avatar's mouth animation.
/// </summary>
public enum LipSyncEngine
{
    /// <summary>Phoneme-based VBridger engine — fast, runs everywhere.</summary>
    VBridger,

    /// <summary>Neural Audio2Face engine — higher fidelity, heavier.</summary>
    Audio2Face,
}

/// <summary>
///     Blendshape solver used by the Audio2Face engine.
/// </summary>
public enum LipSyncSolver
{
    /// <summary>Bounded Variable Least Squares — slower, higher quality.</summary>
    Bvls,

    /// <summary>Projected Gradient Descent — faster, lower latency.</summary>
    Pgd,
}

/// <summary>
///     Configuration for the lip-sync subsystem.
/// </summary>
public record LipSyncOptions
{
    /// <summary>
    ///     Which lip-sync engine to use. Hot-reloadable via <c>IOptionsMonitor</c> —
    ///     changing this swaps the active processor at runtime.
    /// </summary>
    public LipSyncEngine Engine { get; set; } = LipSyncEngine.VBridger;

    public Audio2FaceOptions Audio2Face { get; set; } = new();
}

/// <summary>
///     Configuration for the Audio2Face lip-sync engine.
/// </summary>
public record Audio2FaceOptions
{
    /// <summary>
    ///     The identity (character) to use when generating lip-sync data.
    /// </summary>
    public string Identity { get; set; } = "James";

    /// <summary>
    ///     Whether to use the GPU for inference.
    /// </summary>
    public bool UseGpu { get; set; }

    /// <summary>
    ///     Which blendshape solver to use.
    /// </summary>
    public LipSyncSolver SolverType { get; set; } = LipSyncSolver.Bvls;
}
