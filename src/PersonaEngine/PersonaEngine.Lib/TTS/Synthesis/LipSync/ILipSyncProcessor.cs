using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.TTS.Synthesis.LipSync;

/// <summary>
///     Processes an <see cref="AudioSegment" /> and produces a <see cref="LipSyncTimeline" />.
/// </summary>
public interface ILipSyncProcessor
{
    /// <summary>
    ///     Which engine this processor implements. Matched against
    ///     <see cref="LipSyncOptions.Engine" /> when selecting the active processor.
    /// </summary>
    LipSyncEngine EngineId { get; }

    /// <summary>
    ///     Processes the given <paramref name="segment" /> and returns the computed timeline.
    /// </summary>
    LipSyncTimeline Process(AudioSegment segment);

    /// <summary>
    ///     Signals the start of a new sentence. Resets per-sentence bookkeeping
    ///     (e.g. timestamp offsets) without clearing model state (GRU, solver).
    /// </summary>
    void BeginSentence() { }

    /// <summary>
    ///     Resets any internal state accumulated across calls. Called when switching engines.
    /// </summary>
    void Reset();
}
