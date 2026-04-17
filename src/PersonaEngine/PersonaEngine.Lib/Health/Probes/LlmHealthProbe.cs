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
    private readonly object _gate = new();
    private SubsystemStatus _current;
    private bool _disposed;

    public LlmHealthProbe(ILlmConnectionProbe inner)
    {
        _inner = inner;
        _current = Compute();
        _inner.StatusChanged += OnChanged;
    }

    public string Name => "LLM";

    public NavSection TargetPanel => NavSection.LlmConnection;

    public SubsystemStatus Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event Action<SubsystemStatus>? StatusChanged;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _inner.StatusChanged -= OnChanged;
    }

    private void OnChanged(LlmChannel _)
    {
        SubsystemStatus? toFire = null;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var next = Compute();
            if (next.Equals(_current))
            {
                return;
            }

            _current = next;
            toFire = next;
        }

        if (toFire is { } status)
        {
            StatusChanged?.Invoke(status);
        }
    }

    private SubsystemStatus Compute()
    {
        var text = _inner.TextStatus;
        var vision = _inner.VisionStatus;

        if (text.Status == LlmProbeStatus.Unknown && vision.Status == LlmProbeStatus.Unknown)
        {
            return new SubsystemStatus(SubsystemHealth.Unknown, "Not tested", null);
        }

        // Text is the load-bearing channel — anything not in the Healthy/Degraded/Unknown
        // buckets (including a surprise Disabled, which LlmConnectionProbe never emits for
        // text today) short-circuits to Failed.
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
            LlmProbeStatus.InvalidUrl => SubsystemHealth.Degraded,
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
