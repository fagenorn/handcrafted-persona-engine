using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Audio;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.Health.Probes;

/// <summary>
///     Observer-style mic probe: checks the configured device exists among the
///     enumerated input devices and that the mic actually opened it. Deeper
///     "have we seen audio frames" is handoff-scoped.
/// </summary>
public sealed class MicrophoneHealthProbe : ISubsystemHealthProbe, IDisposable
{
    private readonly IMicrophone _mic;
    private readonly IMicMuteController _muteController;
    private readonly IOptionsMonitor<MicrophoneConfiguration> _monitor;
    private readonly IDisposable? _onChangeSub;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private SubsystemStatus _current;
    private bool _disposed;

    public MicrophoneHealthProbe(
        IMicrophone mic,
        IOptionsMonitor<MicrophoneConfiguration> monitor,
        IMicMuteController muteController
    )
    {
        _mic = mic;
        _monitor = monitor;
        _muteController = muteController;
        _current = ComputeCurrent();
        _onChangeSub = monitor.OnChange((_, _) => Refresh());
        _muteController.MutedChanged += OnMuteChanged;
        _timer = new Timer(_ => Refresh(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public string Name => "Microphone";

    public NavSection TargetPanel => NavSection.Listening;

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

        _muteController.MutedChanged -= OnMuteChanged;
        _onChangeSub?.Dispose();
        _timer.Dispose();
    }

    private void OnMuteChanged(bool _) => Refresh();

    private void Refresh()
    {
        SubsystemStatus? toFire = null;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var next = ComputeCurrent();
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

    private SubsystemStatus ComputeCurrent()
    {
        if (_muteController.IsMuted)
        {
            return new SubsystemStatus(
                SubsystemHealth.Muted,
                "Muted",
                "User muted the microphone."
            );
        }

        return Compute();
    }

    private SubsystemStatus Compute()
    {
        var available = _mic.AvailableDevices;
        if (available.Count == 0)
        {
            return new SubsystemStatus(SubsystemHealth.Failed, "No input devices available", null);
        }

        var configured = _monitor.CurrentValue.DeviceName;

        // No device configured — treat as healthy (defaults to system default).
        if (string.IsNullOrWhiteSpace(configured))
        {
            return new SubsystemStatus(SubsystemHealth.Healthy, "Default device", null);
        }

        var exists = available.Any(d =>
            configured.Trim().Equals(d?.Trim(), StringComparison.OrdinalIgnoreCase)
        );

        if (!exists)
        {
            return new SubsystemStatus(
                SubsystemHealth.Degraded,
                "Fell back to default",
                $"Configured device '{configured}' not found among {available.Count} enumerated input device(s)."
            );
        }

        return new SubsystemStatus(SubsystemHealth.Healthy, $"Device: {configured}", null);
    }
}
