using PersonaEngine.Lib.TTS.Synthesis.Engine;

namespace PersonaEngine.Lib.Configuration;

public record TtsConfiguration
{
    /// <summary>
    ///     Which TTS engine to use. Must match an <see cref="ISentenceSynthesizer.EngineId" />.
    ///     Hot-reloadable via <c>IOptionsMonitor</c> — changing this swaps the active engine at runtime.
    /// </summary>
    public string ActiveEngine { get; init; } = "kokoro";

    public string ModelDirectory { get; init; } =
        Path.Combine(Directory.GetCurrentDirectory(), "Resources", "Models");

    public string EspeakPath { get; init; } = "espeak-ng";

    public AuditionSampleOptions AuditionSample { get; init; } = new();

    public KokoroVoiceOptions Kokoro { get; init; } = new();

    public RVCFilterOptions Rvc { get; init; } = new();

    public Qwen3TtsOptions Qwen3 { get; init; } = new();
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
