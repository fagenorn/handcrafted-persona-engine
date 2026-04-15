namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Provides smoothed microphone amplitude signal for UI meters. Subscribes to
///     <see cref="IMicrophone.SamplesAvailable" /> and exposes a current value plus a
///     short trailing history for sparkline drawing.
/// </summary>
public interface IMicrophoneAmplitudeProvider
{
    /// <summary>Current smoothed RMS amplitude in <c>[0, 1]</c>. Updated at mic buffer cadence (~30 Hz).</summary>
    float CurrentAmplitude { get; }

    /// <summary>
    ///     Ring buffer of the most recent amplitude samples, one entry per mic buffer event.
    ///     Indexed by raw slot — see <see cref="HistoryHead" /> for the next-write position.
    /// </summary>
    ReadOnlySpan<float> History { get; }

    /// <summary>Next-write index into <see cref="History" />. Newest sample is at <c>(HistoryHead - 1 + History.Length) % History.Length</c>.</summary>
    int HistoryHead { get; }
}
