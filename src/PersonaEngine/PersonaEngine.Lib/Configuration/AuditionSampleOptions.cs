namespace PersonaEngine.Lib.Configuration;

/// <summary>
///     Canned sample text used by the voice-audition feature. One phrase per engine,
///     chosen to exercise each engine's strengths (phoneme clarity for Kokoro,
///     emotional range for Qwen3).
/// </summary>
public sealed record AuditionSampleOptions
{
    public string Kokoro { get; init; } = "Hello there — I'm your new companion.";
    public string Qwen3 { get; init; } = "Hello there — I'm your new companion.";
}
