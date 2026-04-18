namespace PersonaEngine.Lib.ASR.VAD;

/// <summary>
///     Exposes the raw per-batch Silero VAD probability for UI consumers. Subscribes to
///     <see cref="IVadDetector.ProbabilityObserved" />.
/// </summary>
public interface IVadProbabilityProvider
{
    /// <summary>Latest VAD probability in <c>[0, 1]</c>. Updated at ~31 Hz during capture.</summary>
    float CurrentProbability { get; }

    /// <summary>Ring buffer of recent probability samples for sparkline drawing.</summary>
    ReadOnlySpan<float> History { get; }

    /// <summary>Next-write index into <see cref="History" />.</summary>
    int HistoryHead { get; }
}
