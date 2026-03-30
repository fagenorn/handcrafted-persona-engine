namespace PersonaEngine.Lib.Audio;

public interface IMemoryBackedAudioSource
{
    public bool StoresFloats { get; }

    public bool StoresBytes { get; }
}
