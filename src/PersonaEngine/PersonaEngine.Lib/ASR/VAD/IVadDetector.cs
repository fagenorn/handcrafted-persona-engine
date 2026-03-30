using PersonaEngine.Lib.Audio;

namespace PersonaEngine.Lib.ASR.VAD;

/// <summary>
///     Represents a voice activity detection component that can detect voice activity segments in mono-channel audio
///     samples at 16 kHz.
/// </summary>
public interface IVadDetector
{
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
