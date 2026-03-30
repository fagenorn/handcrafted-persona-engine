namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Represents the format of audio data.
/// </summary>
public readonly struct AudioFormat
{
    /// <summary>
    ///     The number of channels in the audio.
    /// </summary>
    public readonly ushort Channels;

    /// <summary>
    ///     The number of bits per sample.
    /// </summary>
    public readonly ushort BitsPerSample;

    /// <summary>
    ///     The sample rate in Hz.
    /// </summary>
    public readonly uint SampleRate;

    /// <summary>
    ///     Creates a new audio format.
    /// </summary>
    public AudioFormat(ushort channels, ushort bitsPerSample, uint sampleRate)
    {
        Channels = channels;
        BitsPerSample = bitsPerSample;
        SampleRate = sampleRate;
    }

    /// <summary>
    ///     Gets the number of bytes per sample.
    /// </summary>
    public int BytesPerSample => BitsPerSample / 8;

    /// <summary>
    ///     Gets the number of bytes per frame (a frame contains one sample for each channel).
    /// </summary>
    public int BytesPerFrame => BytesPerSample * Channels;

    /// <summary>
    ///     Creates a mono format with the specified bits per sample and sample rate.
    /// </summary>
    public static AudioFormat CreateMono(ushort bitsPerSample, uint sampleRate)
    {
        return new AudioFormat(1, bitsPerSample, sampleRate);
    }

    /// <summary>
    ///     Creates a stereo format with the specified bits per sample and sample rate.
    /// </summary>
    public static AudioFormat CreateStereo(ushort bitsPerSample, uint sampleRate)
    {
        return new AudioFormat(2, bitsPerSample, sampleRate);
    }
}
