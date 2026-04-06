namespace PersonaEngine.Lib.TTS.Synthesis.Audio;

public interface IAudioFilter
{
    int Priority { get; }

    void Process(AudioSegment audioSegment);
}

/// <summary>
///     An audio filter that requires a minimum number of samples to produce
///     correct output. The pipeline will transparently accumulate streaming
///     chunks until the threshold is met before calling <see cref="IAudioFilter.Process" />.
///     Adjacent windows are stitched using SOLA (Synchronized Overlap-Add) to
///     eliminate boundary artifacts — the filter itself does not need to worry
///     about crossfading or overlap context.
/// </summary>
public interface IBufferedAudioFilter : IAudioFilter
{
    /// <summary>
    ///     Minimum number of audio samples this filter needs for correct processing.
    ///     The pipeline will buffer incoming segments until this threshold is
    ///     reached, then deliver a single merged <see cref="AudioSegment" />.
    /// </summary>
    int MinimumSampleCount { get; }
}
