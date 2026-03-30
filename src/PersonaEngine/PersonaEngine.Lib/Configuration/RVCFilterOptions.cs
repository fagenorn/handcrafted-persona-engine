namespace PersonaEngine.Lib.Configuration;

public record RVCFilterOptions
{
    public string DefaultVoice { get; init; } = "KasumiVA";

    public bool Enabled { get; set; } = true;

    public int HopSize { get; set; } = 64;

    public int SpeakerId { get; set; } = 0;

    public int F0UpKey { get; set; } = 0;
}
