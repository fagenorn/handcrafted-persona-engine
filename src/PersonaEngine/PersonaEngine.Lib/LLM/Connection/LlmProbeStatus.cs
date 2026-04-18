namespace PersonaEngine.Lib.LLM.Connection;

/// <summary>
///     Result of the most recent <see cref="ILlmConnectionProbe.ProbeAsync" /> call
///     for a given <see cref="LlmChannel" />.
/// </summary>
public enum LlmProbeStatus
{
    /// <summary>No probe has completed yet.</summary>
    Unknown,

    /// <summary>A probe is in flight for this channel.</summary>
    Probing,

    /// <summary>Endpoint replied and is serving the configured model.</summary>
    Reachable,

    /// <summary>Endpoint replied but does not serve the configured model id.</summary>
    ModelMissing,

    /// <summary>HTTP 401/403 — API key missing, malformed, or rejected.</summary>
    Unauthorized,

    /// <summary>Network error, timeout, or non-success HTTP status (other than 401/403).</summary>
    Unreachable,

    /// <summary>Configured endpoint is not a well-formed absolute http/https URL.</summary>
    InvalidUrl,

    /// <summary>Channel is disabled in config and intentionally not probed.</summary>
    Disabled,
}

/// <summary>Latest observation for one <see cref="LlmChannel" /> probe.</summary>
/// <param name="Status">Terminal (or in-flight) probe status.</param>
/// <param name="DetailMessage">Optional human-readable detail for tooltips / logs.</param>
/// <param name="AvailableModels">Model ids returned by <c>GET /models</c>; empty on failure.</param>
/// <param name="ProbedAt">UTC timestamp the status was recorded.</param>
public readonly record struct LlmProbeResult(
    LlmProbeStatus Status,
    string? DetailMessage,
    IReadOnlyList<string> AvailableModels,
    DateTimeOffset ProbedAt
)
{
    /// <summary>Sentinel value representing "no probe has run yet".</summary>
    public static LlmProbeResult Unknown { get; } =
        new(LlmProbeStatus.Unknown, null, Array.Empty<string>(), DateTimeOffset.MinValue);
}
