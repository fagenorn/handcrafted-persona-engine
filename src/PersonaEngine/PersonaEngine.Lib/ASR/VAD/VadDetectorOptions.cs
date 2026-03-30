namespace PersonaEngine.Lib.ASR.VAD;

public class VadDetectorOptions
{
    public TimeSpan MinSpeechDuration { get; set; } = TimeSpan.FromMilliseconds(150);

    public TimeSpan MinSilenceDuration { get; set; } = TimeSpan.FromMilliseconds(150);
}
