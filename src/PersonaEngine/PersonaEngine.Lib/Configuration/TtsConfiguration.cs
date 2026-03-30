namespace PersonaEngine.Lib.Configuration;

public record TtsConfiguration
{
    public string ModelDirectory { get; init; } =
        Path.Combine(Directory.GetCurrentDirectory(), "Resources", "Models");

    public string EspeakPath { get; init; } = "espeak-ng";

    public KokoroVoiceOptions Voice { get; init; } = new();

    public RVCFilterOptions Rvc { get; init; } = new();
}

public record KokoroVoiceOptions
{
    public string DefaultVoice { get; init; } = "af_heart";

    public bool UseBritishEnglish { get; init; } = false;

    public float DefaultSpeed { get; init; } = 1.0f;

    public int MaxPhonemeLength { get; init; } = 510;

    public int SampleRate { get; init; } = 24000;

    public bool TrimSilence { get; init; } = false;
}
