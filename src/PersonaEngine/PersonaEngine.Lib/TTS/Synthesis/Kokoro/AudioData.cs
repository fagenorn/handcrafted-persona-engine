namespace PersonaEngine.Lib.TTS.Synthesis.Kokoro;

/// <summary>
///     Result of audio synthesis
/// </summary>
public class AudioData(Memory<float> samples, ReadOnlyMemory<long> phonemeTimings)
{
    /// <summary>
    ///     Audio samples
    /// </summary>
    public Memory<float> Samples { get; } = samples;

    /// <summary>
    ///     Durations for phoneme timing
    /// </summary>
    public ReadOnlyMemory<long> PhonemeTimings { get; } = phonemeTimings;
}
