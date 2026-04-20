namespace PersonaEngine.Lib.Configuration;

/// <summary>
///     Configuration for the Qwen3-TTS engine.
///     Bound to "Config:Tts:Qwen3" in appsettings.json.
/// </summary>
public record Qwen3TtsOptions
{
    public string Speaker { get; init; } = "ryan";

    public string Language { get; init; } = "english";

    public string? Instruct { get; init; }

    public float Temperature { get; init; } = 0.6f;

    public int TopK { get; init; } = 50;

    public float TopP { get; init; } = 1.0f;

    public float RepetitionPenalty { get; init; } = 1.05f;

    public int MaxNewTokens { get; init; } = 2048;

    public int EmitEveryFrames { get; init; } = 8;

    /// <summary>
    ///     When true, the Code Predictor (groups 1-15) uses argmax/greedy decoding
    ///     instead of temperature + top-K sampling. Some Rust implementations use
    ///     greedy for cleaner spectral detail; the official Python repo uses sampling.
    /// </summary>
    public bool CodePredictorGreedy { get; init; } = false;

    /// <summary>
    ///     When true, applies a progressive logit penalty to low-token-ID frames
    ///     after consecutive silent frames, preventing long silent tails.
    ///     This is a custom heuristic not present in the official implementation.
    /// </summary>
    public bool SilencePenaltyEnabled { get; init; } = false;
}
