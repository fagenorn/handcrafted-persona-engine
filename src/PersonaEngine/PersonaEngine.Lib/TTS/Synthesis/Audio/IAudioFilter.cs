namespace PersonaEngine.Lib.TTS.Synthesis;

public interface IAudioFilter
{
    int Priority { get; }

    void Process(AudioSegment audioSegment);
}
