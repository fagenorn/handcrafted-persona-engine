using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.TTS.Synthesis.LipSync;

/// <summary>
///     Processes an <see cref="AudioSegment" /> and produces a <see cref="LipSyncTimeline" />.
/// </summary>
public interface ILipSyncProcessor
{
    /// <summary>
    ///     Unique identifier for this processor. Used to match against <c>LipSyncOptions.Engine</c>.
    /// </summary>
    string EngineId { get; }

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
