using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.Rendering.Spout;

namespace PersonaEngine.Lib.Health.Probes;

/// <summary>
///     Adapts <see cref="ISpoutRegistry" /> onto the coarse Dashboard health
///     buckets. Compares the number of configured Spout senders against the
///     number currently publishing frames:
///     <list type="bullet">
///         <item>0 configured → <see cref="SubsystemHealth.Disabled" /> ("No senders").</item>
///         <item>0 active of N configured → <see cref="SubsystemHealth.Failed" /> ("No active senders").</item>
///         <item>Some but not all active → <see cref="SubsystemHealth.Degraded" /> ("Partial").</item>
///         <item>All configured senders active → <see cref="SubsystemHealth.Healthy" /> ("Streaming").</item>
///     </list>
/// </summary>
public sealed class SpoutHealthProbe : ISubsystemHealthProbe, IDisposable
{
    private readonly ISpoutRegistry _registry;
    private readonly object _gate = new();
    private SubsystemStatus _current;
    private bool _disposed;

    public SpoutHealthProbe(ISpoutRegistry registry)
    {
        _registry = registry;
        _current = Compute();
        _registry.SendersChanged += OnSendersChanged;
    }

    public string Name => "Spout";

    public NavSection TargetPanel => NavSection.Streaming;

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

        _registry.SendersChanged -= OnSendersChanged;
    }

    private void OnSendersChanged()
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
        var configured = _registry.ConfiguredSenderCount;
        var active = _registry.ActiveSenderCount;

        if (configured == 0)
        {
            return new SubsystemStatus(SubsystemHealth.Disabled, "No senders", null);
        }

        if (active == 0)
        {
            return new SubsystemStatus(
                SubsystemHealth.Failed,
                "No active senders",
                $"0 of {configured} sender(s) created."
            );
        }

        if (active < configured)
        {
            return new SubsystemStatus(
                SubsystemHealth.Degraded,
                "Partial",
                $"{active} of {configured} sender(s) active."
            );
        }

        return new SubsystemStatus(SubsystemHealth.Healthy, "Streaming", null);
    }
}
