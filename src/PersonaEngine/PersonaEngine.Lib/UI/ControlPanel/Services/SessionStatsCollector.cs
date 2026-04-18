using System.Diagnostics.Metrics;
using PersonaEngine.Lib.Core.Conversation.Implementations.Metrics;

namespace PersonaEngine.Lib.UI.ControlPanel.Services;

/// <summary>
///     Listens to <see cref="ConversationMetrics" /> instruments via <see cref="MeterListener" />
///     and accumulates running totals for UI display. Thread-safe: counter callbacks use
///     <see cref="Interlocked" />, latency average uses a lock.
/// </summary>
public sealed class SessionStatsCollector : IDisposable
{
    private const string TurnsStartedName = "personaengine.conversation.turns.started.count";

    private const string TurnsInterruptedName =
        "personaengine.conversation.turns.interrupted.count";

    private const string FirstAudioLatencyName =
        "personaengine.conversation.turn.first_audio_latency";

    private readonly object _latencyLock = new();

    private readonly MeterListener _listener;

    private bool _firstLatencySkipped;
    private long _latencyCount;
    private double _latencySum;

    private long _turnsInterrupted;
    private long _turnsStarted;

    public SessionStatsCollector(IMeterFactory meterFactory)
    {
        _listener = new MeterListener();

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name != ConversationMetrics.MeterName)
                return;

            if (
                instrument.Name is TurnsStartedName or TurnsInterruptedName or FirstAudioLatencyName
            )
                listener.EnableMeasurementEvents(instrument);
        };

        _listener.SetMeasurementEventCallback<long>(OnCounterMeasurement);
        _listener.SetMeasurementEventCallback<double>(OnHistogramMeasurement);

        _listener.Start();
    }

    public long TurnsStarted => Interlocked.Read(ref _turnsStarted);

    public long TurnsInterrupted => Interlocked.Read(ref _turnsInterrupted);

    public double? AvgFirstAudioLatencyMs
    {
        get
        {
            lock (_latencyLock)
            {
                return _latencyCount > 0 ? _latencySum / _latencyCount : null;
            }
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    private void OnCounterMeasurement(
        Instrument instrument,
        long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state
    )
    {
        if (instrument.Name == TurnsStartedName)
            Interlocked.Add(ref _turnsStarted, measurement);
        else if (instrument.Name == TurnsInterruptedName)
            Interlocked.Add(ref _turnsInterrupted, measurement);
    }

    private void OnHistogramMeasurement(
        Instrument instrument,
        double measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state
    )
    {
        if (instrument.Name != FirstAudioLatencyName)
            return;

        lock (_latencyLock)
        {
            if (!_firstLatencySkipped)
            {
                _firstLatencySkipped = true;
                return;
            }

            _latencySum += measurement;
            _latencyCount++;
        }
    }
}
