namespace PersonaEngine.Lib.UI.ControlPanel.Panels.LlmConnection;

using PersonaEngine.Lib.Health;
using PersonaEngine.Lib.LLM.Connection;

internal static class LlmProbeStatusAdapter
{
    /// <summary>
    ///     Projects a probe status + optional detail string into the shared
    ///     <see cref="SubsystemStatus" /> shape consumed by
    ///     <see cref="Panels.Shared.SubsystemStatusChip" />.
    /// </summary>
    public static SubsystemStatus ToSubsystemStatus(LlmProbeStatus status, string? detail)
    {
        var (health, label) = status switch
        {
            LlmProbeStatus.Unknown => (SubsystemHealth.Unknown, "Not tested yet"),
            LlmProbeStatus.Probing => (SubsystemHealth.Unknown, "Testing\u2026"),
            LlmProbeStatus.Reachable => (SubsystemHealth.Healthy, "Ready"),
            LlmProbeStatus.ModelMissing => (SubsystemHealth.Degraded, "Model not found"),
            LlmProbeStatus.Unauthorized => (SubsystemHealth.Failed, "Auth failed"),
            LlmProbeStatus.Unreachable => (SubsystemHealth.Failed, "Unreachable"),
            LlmProbeStatus.InvalidUrl => (SubsystemHealth.Failed, "Invalid URL"),
            LlmProbeStatus.Disabled => (SubsystemHealth.Disabled, "Off"),
            _ => (SubsystemHealth.Unknown, "?"),
        };
        return new SubsystemStatus(health, label, detail);
    }
}
