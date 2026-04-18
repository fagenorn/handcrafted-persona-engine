using PersonaEngine.Lib.Audio;

namespace PersonaEngine.Lib.ASR.VAD;

/// <summary>
///     Represents a voice activity detection component that can detect voice activity segments in mono-channel audio
///     samples at 16 kHz.
/// </summary>
public interface IVadDetector
{
    /// <summary>
    ///     Raised for each VAD inference batch with the raw probability in <c>[0, 1]</c>.
    ///     Fires at ~31 Hz while the detector is running (once per 512-sample batch at 16 kHz).
    /// </summary>
    event Action<float>? ProbabilityObserved;

    /// <summary>
    ///     Detects voice activity segments in the given audio source.
    /// </summary>
    /// <param name="source">The audio source to analyze.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    IAsyncEnumerable<VadSegment> DetectSegmentsAsync(
        IAudioSource source,
        CancellationToken cancellationToken
    );
}
