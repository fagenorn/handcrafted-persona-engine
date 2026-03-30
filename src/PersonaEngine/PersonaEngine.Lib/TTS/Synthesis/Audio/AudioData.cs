namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Result of audio synthesis
/// </summary>
public class AudioData
{
    public AudioData(Memory<float> samples, ReadOnlyMemory<long> phonemeTimings)
    {
        Samples = samples;
        PhonemeTimings = phonemeTimings;
    }

    /// <summary>
    ///     Audio samples
    /// </summary>
    public Memory<float> Samples { get; }

    /// <summary>
    ///     Durations for phoneme timing
    /// </summary>
    public ReadOnlyMemory<long> PhonemeTimings { get; }
}
