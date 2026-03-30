namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Details of the audio source header.
/// </summary>
public class AudioSourceHeader
{
    /// <summary>
    ///     Gets the number of channels in the current wave file.
    /// </summary>
    public ushort Channels { get; set; }

    /// <summary>
    ///     Gets the Sample Rate in the current wave file.
    /// </summary>
    public uint SampleRate { get; set; }

    /// <summary>
    ///     Gets the Bits Per Sample in the current wave file.
    /// </summary>
    public ushort BitsPerSample { get; set; }
}
