namespace PersonaEngine.Lib.TTS.Synthesis.LipSync;

/// <summary>
///     Provides the currently active <see cref="ILipSyncProcessor" />.
///     Implementations may hot-reload the active processor at runtime.
/// </summary>
public interface ILipSyncProcessorProvider
{
    /// <summary>
    ///     The currently active lip-sync processor.
    /// </summary>
    ILipSyncProcessor Current { get; }
}
