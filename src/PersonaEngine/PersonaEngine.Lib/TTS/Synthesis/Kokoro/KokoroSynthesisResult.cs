namespace PersonaEngine.Lib.TTS.Synthesis.Kokoro;

/// <summary>
///     Result of Kokoro ONNX audio synthesis (waveform + phoneme duration tensor).
/// </summary>
public class KokoroSynthesisResult(Memory<float> samples, ReadOnlyMemory<long> phonemeTimings)
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
