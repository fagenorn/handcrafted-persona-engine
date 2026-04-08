namespace PersonaEngine.Lib.Configuration;

/// <summary>
///     Configuration for the lip-sync subsystem.
/// </summary>
public class LipSyncOptions
{
    /// <summary>
    ///     Which lip-sync engine to use. Must match an <see cref="PersonaEngine.Lib.TTS.Synthesis.LipSync.ILipSyncProcessor.EngineId" />.
    ///     Hot-reloadable via <c>IOptionsMonitor</c> — changing this swaps the active processor at runtime.
    /// </summary>
    public string Engine { get; set; } = "VBridger";
}

/// <summary>
///     Configuration for the Audio2Face lip-sync engine.
/// </summary>
public class Audio2FaceOptions
{
    /// <summary>
    ///     The identity (character) to use when generating lip-sync data.
    /// </summary>
    public string Identity { get; set; } = "Claire";

    /// <summary>
    ///     Whether to use the GPU for inference.
    /// </summary>
    public bool UseGpu { get; set; }
}
