namespace PersonaEngine.Lib.TTS.Synthesis.Engine;

/// <summary>
///     A scoped, stateful synthesis session for a single conversation turn.
///     Implementations may carry state (e.g., audio decoder context) across
///     sentences within the same turn for improved prosodic continuity.
/// </summary>
public interface ISynthesisSession : IAsyncDisposable
{
    /// <summary>
    ///     Synthesizes a single sentence within this session.
    /// </summary>
    /// <param name="sentence">Pre-filtered text sentence to synthesize.</param>
    /// <param name="isLastSegment">
    ///     True if this is the final sentence in the turn. Implementations should
    ///     flush any buffered state (e.g., decoder conv history) on the last segment.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Stream of audio segments, each carrying token timing data.</returns>
    IAsyncEnumerable<AudioSegment> SynthesizeAsync(
        string sentence,
        bool isLastSegment,
        CancellationToken cancellationToken = default
    );
}
