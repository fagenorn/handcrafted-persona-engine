using PersonaEngine.Lib.LLM.Connection;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.Health.Probes;

/// <summary>
///     Adapts <see cref="ILlmConnectionProbe" /> (rich LLM-specific status) to
///     <see cref="ISubsystemHealthProbe" /> (coarse Dashboard buckets). Text is
///     the load-bearing channel — it decides Healthy/Failed. Vision being off is
///     fine; vision being broken with text still Reachable is Degraded.
/// </summary>
public sealed class LlmHealthProbe : ISubsystemHealthProbe, IDisposable
{
    private readonly ILlmConnectionProbe _inner;
    private SubsystemStatus _current;

    public LlmHealthProbe(ILlmConnectionProbe inner)
    {
        _inner = inner;
        _current = Compute();
        _inner.StatusChanged += OnChanged;
    }

    public string Name => "LLM";

    public NavSection TargetPanel => NavSection.LlmConnection;

    public SubsystemStatus Current => _current;

    public event Action<SubsystemStatus>? StatusChanged;

    public void Dispose() => _inner.StatusChanged -= OnChanged;

    private void OnChanged(LlmChannel _)
    {
        var next = Compute();
        if (next.Equals(_current))
        {
            return;
        }

        _current = next;
        StatusChanged?.Invoke(next);
    }

    private SubsystemStatus Compute()
    {
        var text = _inner.TextStatus;
        var vision = _inner.VisionStatus;

        if (text.Status == LlmProbeStatus.Unknown && vision.Status == LlmProbeStatus.Unknown)
        {
            return new SubsystemStatus(SubsystemHealth.Unknown, "Not tested", null);
        }

        var textHealth = text.Status switch
        {
            LlmProbeStatus.Reachable => SubsystemHealth.Healthy,
            LlmProbeStatus.ModelMissing => SubsystemHealth.Degraded,
            LlmProbeStatus.Probing => SubsystemHealth.Unknown,
            LlmProbeStatus.Unknown => SubsystemHealth.Unknown,
            _ => SubsystemHealth.Failed,
        };

        if (textHealth == SubsystemHealth.Failed)
        {
            return new SubsystemStatus(
                SubsystemHealth.Failed,
                LabelFor(text.Status),
                text.DetailMessage
            );
        }

        var visionHealth = vision.Status switch
        {
            LlmProbeStatus.Reachable => SubsystemHealth.Healthy,
            LlmProbeStatus.Disabled => SubsystemHealth.Healthy,
            LlmProbeStatus.ModelMissing => SubsystemHealth.Degraded,
            LlmProbeStatus.Probing => SubsystemHealth.Unknown,
            LlmProbeStatus.Unknown => SubsystemHealth.Unknown,
            _ => SubsystemHealth.Degraded,
        };

        var overall = (textHealth, visionHealth) switch
        {
            (SubsystemHealth.Healthy, SubsystemHealth.Healthy) => SubsystemHealth.Healthy,
            (SubsystemHealth.Degraded, _) => SubsystemHealth.Degraded,
            (_, SubsystemHealth.Degraded) => SubsystemHealth.Degraded,
            _ => SubsystemHealth.Unknown,
        };

        return new SubsystemStatus(overall, LabelFor(text.Status), text.DetailMessage);
    }

    private static string LabelFor(LlmProbeStatus status) =>
        status switch
        {
            LlmProbeStatus.Reachable => "Ready",
            LlmProbeStatus.ModelMissing => "Model not found",
            LlmProbeStatus.Unauthorized => "Auth failed",
            LlmProbeStatus.Unreachable => "Unreachable",
            LlmProbeStatus.InvalidUrl => "Invalid URL",
            LlmProbeStatus.Probing => "Testing…",
            LlmProbeStatus.Disabled => "Off",
            _ => "Not tested",
        };
}
