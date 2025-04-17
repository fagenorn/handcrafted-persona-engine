namespace PersonaEngine.Lib.TTS.Synthesis;

public record AudioSegment
{
    public AudioSegment(Memory<float> audioData, int sampleRate, IReadOnlyList<Token> tokens)
    {
        AudioData  = audioData;
        SampleRate = sampleRate;
        Channels   = 1;
        Tokens     = tokens ?? Array.Empty<Token>();
    }

    public Guid Id { get; init; } = Guid.NewGuid();

    public Memory<float> AudioData { get; set; }

    public int SampleRate { get; set; }

    public float DurationInSeconds => AudioData.Length / (float)SampleRate;

    public IReadOnlyList<Token> Tokens { get; }

    public int Channels { get; }
}