namespace PersonaEngine.Lib.LLM.Connection;

public enum LlmProbeStatus
{
    Unknown,
    Probing,
    Reachable,
    ModelMissing,
    Unauthorized,
    Unreachable,
    InvalidUrl,
    Disabled,
}

public readonly record struct LlmProbeResult(
    LlmProbeStatus Status,
    string? DetailMessage,
    IReadOnlyList<string> AvailableModels,
    DateTimeOffset ProbedAt
)
{
    public static LlmProbeResult Unknown { get; } =
        new(LlmProbeStatus.Unknown, null, Array.Empty<string>(), DateTimeOffset.MinValue);
}
