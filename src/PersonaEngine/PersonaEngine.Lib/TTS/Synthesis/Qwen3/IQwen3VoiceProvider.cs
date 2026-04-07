namespace PersonaEngine.Lib.TTS.Synthesis.Qwen3;

public interface IQwen3VoiceProvider
{
    IReadOnlyList<string> GetAvailableSpeakers();
}
