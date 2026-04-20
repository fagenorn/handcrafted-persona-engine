using System.Diagnostics;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Session;

public enum MetricPhase
{
    Llm,
    Tts,
    AudioPlayback,
}

internal readonly record struct TurnTimingSummary(
    double? TurnDurationMs,
    double? SttLatencyMs,
    double? FirstLlmTokenMs,
    double? FirstTtsChunkMs,
    double? FirstAudioMs,
    double? LlmDurationMs,
    double? TtsDurationMs,
    double? AudioDurationMs,
    CompletionReason LlmFinishReason,
    CompletionReason TtsFinishReason,
    CompletionReason AudioFinishReason
);

internal struct TurnMetricsTracker
{
    private Stopwatch? _turnStopwatch;
    private Stopwatch? _firstLlmTokenLatencyStopwatch;
    private Stopwatch? _firstTtsChunkLatencyStopwatch;
    private Stopwatch? _firstAudioLatencyStopwatch;

    private readonly Dictionary<MetricPhase, Stopwatch> _stopwatches = [];

    private double? _firstLlmTokenLatencyMs;
    private double? _firstTtsChunkLatencyMs;
    private double? _firstAudioLatencyMs;
    private DateTimeOffset? _sttStartTime;
    private double? _sttLatencyMs;

    public CompletionReason LlmFinishReason;
    public CompletionReason TtsFinishReason;
    public CompletionReason AudioFinishReason;

    public double? FirstLlmTokenLatencyMs => _firstLlmTokenLatencyMs;
    public double? FirstTtsChunkLatencyMs => _firstTtsChunkLatencyMs;
    public double? FirstAudioLatencyMs => _firstAudioLatencyMs;
    public double? SttLatencyMs => _sttLatencyMs;
    public Stopwatch? TurnStopwatch => _turnStopwatch;
    public Stopwatch? FirstAudioLatencyStopwatch => _firstAudioLatencyStopwatch;

    /// <summary>
    /// Returns the elapsed time since the turn started without stopping the stopwatch.
    /// </summary>
    public double? GetTurnElapsedMs() => _turnStopwatch?.Elapsed.TotalMilliseconds;

    public TurnMetricsTracker() { }

    public void StartTurn(double sttProcessingDurationMs)
    {
        _turnStopwatch = Stopwatch.StartNew();
        _firstLlmTokenLatencyStopwatch = Stopwatch.StartNew();
        _sttLatencyMs = sttProcessingDurationMs;

        LlmFinishReason = CompletionReason.Completed;
        TtsFinishReason = CompletionReason.Completed;
        AudioFinishReason = CompletionReason.Completed;

        _firstLlmTokenLatencyMs = null;
        _firstTtsChunkLatencyMs = null;
        _firstAudioLatencyMs = null;
    }

    public void RecordSttStart(DateTimeOffset timestamp)
    {
        _sttStartTime = timestamp;
    }

    public DateTimeOffset? GetSttStartAndClear()
    {
        var start = _sttStartTime;
        _sttStartTime = null;

        return start;
    }

    public bool HasSttStart => _sttStartTime.HasValue;

    /// <summary>
    /// Records first LLM token latency. Stops LLM latency stopwatch, starts TTS latency stopwatch.
    /// Returns the recorded latency in ms, or null if already recorded.
    /// </summary>
    public double? RecordFirstLlmToken()
    {
        if (_firstLlmTokenLatencyStopwatch == null)
        {
            return null;
        }

        _firstLlmTokenLatencyStopwatch.Stop();
        _firstLlmTokenLatencyMs = _firstLlmTokenLatencyStopwatch.Elapsed.TotalMilliseconds;
        _firstLlmTokenLatencyStopwatch = null;
        _firstTtsChunkLatencyStopwatch = Stopwatch.StartNew();

        return _firstLlmTokenLatencyMs;
    }

    /// <summary>
    /// Records first TTS chunk latency. Stops TTS latency stopwatch, starts audio latency stopwatch.
    /// Returns the recorded latency in ms, or null if already recorded.
    /// </summary>
    public double? RecordFirstTtsChunk()
    {
        if (_firstTtsChunkLatencyStopwatch == null)
        {
            return null;
        }

        _firstTtsChunkLatencyStopwatch.Stop();
        _firstTtsChunkLatencyMs = _firstTtsChunkLatencyStopwatch.Elapsed.TotalMilliseconds;
        _firstTtsChunkLatencyStopwatch = null;
        _firstAudioLatencyStopwatch = Stopwatch.StartNew();

        return _firstTtsChunkLatencyMs;
    }

    /// <summary>
    /// Records first audio chunk latency. Stops audio latency stopwatch.
    /// Returns the recorded latency in ms, or null if already recorded.
    /// </summary>
    public double? RecordFirstAudioChunk()
    {
        if (_firstAudioLatencyStopwatch == null)
        {
            return null;
        }

        _firstAudioLatencyStopwatch.Stop();
        _firstAudioLatencyMs = _firstAudioLatencyStopwatch.Elapsed.TotalMilliseconds;
        _firstAudioLatencyStopwatch = null;

        return _firstAudioLatencyMs;
    }

    public void StartStopwatch(MetricPhase phase)
    {
        _stopwatches[phase] = Stopwatch.StartNew();
    }

    public double? StopStopwatch(MetricPhase phase)
    {
        if (!_stopwatches.Remove(phase, out var sw))
        {
            return null;
        }

        sw.Stop();

        return sw.Elapsed.TotalMilliseconds;
    }

    public double? StopTurnStopwatch()
    {
        if (_turnStopwatch == null)
        {
            return null;
        }

        _turnStopwatch.Stop();

        return _turnStopwatch.Elapsed.TotalMilliseconds;
    }

    public void ClearNonAudioTtsTracking()
    {
        _firstTtsChunkLatencyStopwatch?.Stop();
        _firstTtsChunkLatencyStopwatch = null;
        _firstTtsChunkLatencyMs = null;
    }

    public TurnTimingSummary CompleteTurn()
    {
        var summary = new TurnTimingSummary(
            _turnStopwatch?.Elapsed.TotalMilliseconds,
            _sttLatencyMs,
            _firstLlmTokenLatencyMs,
            _firstTtsChunkLatencyMs,
            _firstAudioLatencyMs,
            _stopwatches.GetValueOrDefault(MetricPhase.Llm)?.Elapsed.TotalMilliseconds,
            _stopwatches.GetValueOrDefault(MetricPhase.Tts)?.Elapsed.TotalMilliseconds,
            _stopwatches.GetValueOrDefault(MetricPhase.AudioPlayback)?.Elapsed.TotalMilliseconds,
            LlmFinishReason,
            TtsFinishReason,
            AudioFinishReason
        );

        Reset();

        return summary;
    }

    public void Reset()
    {
        _turnStopwatch?.Stop();
        _firstAudioLatencyStopwatch?.Stop();
        _firstLlmTokenLatencyStopwatch?.Stop();
        _firstTtsChunkLatencyStopwatch?.Stop();

        foreach (var sw in _stopwatches.Values)
        {
            sw.Stop();
        }

        _stopwatches.Clear();

        _turnStopwatch = null;
        _firstAudioLatencyStopwatch = null;
        _firstLlmTokenLatencyStopwatch = null;
        _firstTtsChunkLatencyStopwatch = null;
        _firstLlmTokenLatencyMs = null;
        _firstTtsChunkLatencyMs = null;
        _firstAudioLatencyMs = null;
        _sttStartTime = null;
        _sttLatencyMs = null;

        LlmFinishReason = CompletionReason.Completed;
        TtsFinishReason = CompletionReason.Completed;
        AudioFinishReason = CompletionReason.Completed;
    }
}
