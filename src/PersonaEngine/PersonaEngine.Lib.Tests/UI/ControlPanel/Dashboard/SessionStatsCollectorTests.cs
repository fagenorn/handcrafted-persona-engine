using System.Diagnostics.Metrics;
using PersonaEngine.Lib.Core.Conversation.Implementations.Metrics;
using PersonaEngine.Lib.UI.ControlPanel.Services;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.ControlPanel.Dashboard;

public sealed class SessionStatsCollectorTests : IDisposable
{
    private readonly SessionStatsCollector _collector;
    private readonly IMeterFactory _meterFactory;
    private readonly ConversationMetrics _metrics;

    public SessionStatsCollectorTests()
    {
        _meterFactory = new TestMeterFactory();
        _metrics = new ConversationMetrics(_meterFactory);
        _collector = new SessionStatsCollector(_meterFactory);
    }

    public void Dispose()
    {
        _collector.Dispose();
        _metrics.Dispose();
        (_meterFactory as IDisposable)?.Dispose();
    }

    [Fact]
    public void Initially_all_stats_are_zero_or_null()
    {
        Assert.Equal(0, _collector.TurnsStarted);
        Assert.Equal(0, _collector.TurnsInterrupted);
        Assert.Null(_collector.AvgFirstAudioLatencyMs);
    }

    [Fact]
    public void TurnsStarted_increments_on_counter_recording()
    {
        var sessionId = Guid.NewGuid();

        _metrics.IncrementTurnsStarted(sessionId);
        _metrics.IncrementTurnsStarted(sessionId);
        _metrics.IncrementTurnsStarted(sessionId);

        Assert.Equal(3, _collector.TurnsStarted);
    }

    [Fact]
    public void TurnsInterrupted_increments_on_counter_recording()
    {
        var sessionId = Guid.NewGuid();
        var turnId = Guid.NewGuid();

        _metrics.IncrementTurnsInterrupted(sessionId, turnId);
        _metrics.IncrementTurnsInterrupted(sessionId, turnId);

        Assert.Equal(2, _collector.TurnsInterrupted);
    }

    [Fact]
    public void AvgFirstAudioLatency_skips_first_turn_warmup()
    {
        var sessionId = Guid.NewGuid();

        // First recording is skipped (warmup)
        _metrics.RecordFirstAudioLatency(9999.0, sessionId, Guid.NewGuid());

        Assert.Null(_collector.AvgFirstAudioLatencyMs);
    }

    [Fact]
    public void AvgFirstAudioLatency_computes_running_average_excluding_first()
    {
        var sessionId = Guid.NewGuid();

        _metrics.RecordFirstAudioLatency(100.0, sessionId, Guid.NewGuid()); // skipped (warmup)
        _metrics.RecordFirstAudioLatency(200.0, sessionId, Guid.NewGuid());
        _metrics.RecordFirstAudioLatency(300.0, sessionId, Guid.NewGuid());

        Assert.NotNull(_collector.AvgFirstAudioLatencyMs);
        Assert.Equal(250.0, _collector.AvgFirstAudioLatencyMs!.Value, precision: 1);
    }

    [Fact]
    public void AvgFirstAudioLatency_single_recording_after_warmup()
    {
        var sessionId = Guid.NewGuid();

        _metrics.RecordFirstAudioLatency(9999.0, sessionId, Guid.NewGuid()); // skipped (warmup)
        _metrics.RecordFirstAudioLatency(42.5, sessionId, Guid.NewGuid());

        Assert.Equal(42.5, _collector.AvgFirstAudioLatencyMs!.Value, precision: 1);
    }

    [Fact]
    public void Dispose_stops_listening()
    {
        var sessionId = Guid.NewGuid();
        _collector.Dispose();

        // Recordings after dispose should not throw or update
        _metrics.IncrementTurnsStarted(sessionId);
        Assert.Equal(0, _collector.TurnsStarted);
    }

    /// <summary>
    ///     Minimal <see cref="IMeterFactory" /> that creates real meters without DI overhead.
    /// </summary>
    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (var meter in _meters)
                meter.Dispose();
        }
    }
}
