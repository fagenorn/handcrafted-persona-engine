using PersonaEngine.Lib.Audio;

namespace PersonaEngine.Lib.Utils.Audio;

internal static class AudioValidation
{
    /// <summary>
    ///     Validates that the audio source is mono-channel at 16 kHz sample rate.
    /// </summary>
    public static void RequireMono16kHz(IAudioSource source)
    {
        if (source.ChannelCount != 1)
        {
            throw new NotSupportedException(
                "Only mono-channel audio is supported. Consider one channel aggregation on the audio source."
            );
        }

        if (source.SampleRate != 16000)
        {
            throw new NotSupportedException(
                "Only 16 kHz audio is supported. Consider resampling before calling this transcriptor."
            );
        }
    }
}
