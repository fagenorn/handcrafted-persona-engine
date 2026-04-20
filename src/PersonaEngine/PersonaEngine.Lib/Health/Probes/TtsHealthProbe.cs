using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.Health.Probes;

/// <summary>
///     Observer-style TTS probe. Mirrors <see cref="ITtsEngine.IsReady" /> /
///     <see cref="ITtsEngine.LastInitError" /> onto the coarse Dashboard health
///     buckets surfaced via <see cref="ISubsystemHealthProbe" />.
/// </summary>
public sealed class TtsHealthProbe : ISubsystemHealthProbe, IDisposable
{
    private readonly ITtsEngine _tts;
    private readonly object _gate = new();
    private SubsystemStatus _current;
    private bool _disposed;

    public TtsHealthProbe(ITtsEngine tts)
    {
        _tts = tts;
        _current = Compute();
        _tts.ReadyChanged += OnReadyChanged;
    }

    public string Name => "TTS";

    public NavSection TargetPanel => NavSection.Voice;

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

        _tts.ReadyChanged -= OnReadyChanged;
    }

    private void OnReadyChanged()
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
        if (_tts.IsReady)
        {
            return new SubsystemStatus(SubsystemHealth.Healthy, "Ready", null);
        }

        var error = _tts.LastInitError;
        if (error is null)
        {
            return new SubsystemStatus(SubsystemHealth.Unknown, "Warming up", null);
        }

        return new SubsystemStatus(SubsystemHealth.Failed, "Init failed", error);
    }
}
