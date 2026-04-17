namespace PersonaEngine.Lib.LLM.Connection;

public interface ILlmConnectionProbe
{
    LlmProbeResult TextStatus { get; }

    LlmProbeResult VisionStatus { get; }

    event Action<LlmChannel>? StatusChanged;

    ValueTask ProbeAsync(LlmChannel channel, CancellationToken ct = default);
}
