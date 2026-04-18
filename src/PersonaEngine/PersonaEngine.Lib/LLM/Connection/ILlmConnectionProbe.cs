namespace PersonaEngine.Lib.LLM.Connection;

/// <summary>
///     Live reachability surface for the configured text / vision LLM endpoints.
///     Panels consume <see cref="TextStatus" /> / <see cref="VisionStatus" /> and
///     subscribe to <see cref="StatusChanged" /> to refresh the UI on transitions.
/// </summary>
/// <remarks>
///     <see cref="StatusChanged" /> fires on transitions only — callers that need the
///     current state should read <see cref="TextStatus" /> / <see cref="VisionStatus" />
///     once immediately after subscribing.
/// </remarks>
public interface ILlmConnectionProbe
{
    /// <summary>Latest observation for the text channel.</summary>
    LlmProbeResult TextStatus { get; }

    /// <summary>Latest observation for the vision channel.</summary>
    LlmProbeResult VisionStatus { get; }

    /// <summary>
    ///     Raised when a channel's status changes. A re-probe that yields the same
    ///     status (and the same available-models surface) does not raise the event —
    ///     consumers only learn about transitions, never about repeated confirmations
    ///     of the current state.
    /// </summary>
    event Action<LlmChannel>? StatusChanged;

    /// <summary>
    ///     Probes one channel. Runs <c>GET {endpoint}/models</c> and updates the
    ///     matching status property. Serialized per channel: concurrent callers
    ///     on the same channel queue behind the in-flight probe.
    /// </summary>
    ValueTask ProbeAsync(LlmChannel channel, CancellationToken ct = default);
}
