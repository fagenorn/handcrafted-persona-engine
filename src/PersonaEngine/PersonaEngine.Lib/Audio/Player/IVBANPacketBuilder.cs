namespace PersonaEngine.Lib.Audio.Player;

/// <summary>
///     Builder for VBAN packets.
/// </summary>
public interface IVBANPacketBuilder
{
    /// <summary>
    ///     Builds a VBAN packet from audio data.
    /// </summary>
    byte[] BuildPacket(
        ReadOnlyMemory<float> audioData,
        int sampleRate,
        int samplesPerChannel,
        int channels
    );
}
