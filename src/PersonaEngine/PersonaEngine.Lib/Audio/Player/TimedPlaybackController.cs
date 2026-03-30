using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PersonaEngine.Lib.Audio.Player;

/// <summary>
///     Implementation of IPlaybackController that works with large audio segments.
/// </summary>
public class TimedPlaybackController : IPlaybackController
{
    private readonly int _initialBufferMs;

    private readonly double _latencyThresholdMs;

    private readonly ILogger _logger;

    private readonly Stopwatch _playbackTimer = new();

    // For time updates
    private readonly object _timeLock = new();

    private readonly int _timeUpdateIntervalMs;

    private readonly Timer _timeUpdateTimer;

    // Latency tracking

    private int _currentSampleRate;

    private double _lastReportedLatencyMs;

    private long _startTimeMs; // When playback actually started (system time)

    /// <summary>
    ///     Initializes a new instance of the TimedPlaybackController class.
    /// </summary>
    /// <param name="initialBufferMs">Initial buffer size in milliseconds.</param>
    /// <param name="latencyThresholdMs">Threshold for significant latency changes in milliseconds.</param>
    /// <param name="timeUpdateIntervalMs">Interval in milliseconds for time updates.</param>
    /// <param name="logger">Logger instance.</param>
    public TimedPlaybackController(
        int initialBufferMs = 20,
        double latencyThresholdMs = 20.0,
        int timeUpdateIntervalMs = 20,
        ILogger<TimedPlaybackController>? logger = null
    )
    {
        _initialBufferMs = initialBufferMs;
        _latencyThresholdMs = latencyThresholdMs;
        _timeUpdateIntervalMs = timeUpdateIntervalMs;
        _logger = logger ?? NullLogger<TimedPlaybackController>.Instance;

        // Start a timer to update the playback time periodically
        _timeUpdateTimer = new Timer(UpdateTime, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc />
    public float CurrentTime { get; private set; }

    /// <inheritdoc />
    public long TotalSamplesPlayed { get; private set; }

    /// <inheritdoc />
    public double CurrentLatencyMs { get; private set; }

    /// <inheritdoc />
    public bool HasLatencyInformation { get; private set; }

    /// <summary>
    ///     Event that fires when the playback time changes substantially.
    /// </summary>
    public event EventHandler<float> TimeChanged
    {
        add
        {
            lock (_timeLock)
            {
                TimeChangedInternal += value;
            }
        }
        remove
        {
            lock (_timeLock)
            {
                TimeChangedInternal -= value;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<double>? LatencyChanged;

    /// <inheritdoc />
    public void Reset()
    {
        _playbackTimer.Reset();
        TotalSamplesPlayed = 0;
        CurrentTime = 0;
        _startTimeMs = 0;

        // Stop the time update timer
        _timeUpdateTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc />
    public void UpdateLatency(double latencyMs)
    {
        var oldLatency = CurrentLatencyMs;
        CurrentLatencyMs = latencyMs;
        HasLatencyInformation = true;

        // Only notify if the change is significant
        if (Math.Abs(latencyMs - _lastReportedLatencyMs) >= _latencyThresholdMs)
        {
            _logger.LogInformation(
                "Playback latency updated: {OldLatency:F1}ms -> {NewLatency:F1}ms",
                oldLatency,
                latencyMs
            );

            _lastReportedLatencyMs = latencyMs;
            LatencyChanged?.Invoke(this, latencyMs);
        }
    }

    /// <inheritdoc />
    public Task<bool> SchedulePlaybackAsync(
        int samplesPerChannel,
        int sampleRate,
        CancellationToken cancellationToken
    )
    {
        // Store the sample rate for time calculations
        _currentSampleRate = sampleRate;

        // Update total samples
        TotalSamplesPlayed += samplesPerChannel;

        // Start the playback timer if this is the first call
        if (!_playbackTimer.IsRunning)
        {
            _logger.LogDebug("Starting playback timer");

            // Account for initial buffering
            var initialBufferMs = HasLatencyInformation
                ? Math.Max(50, _initialBufferMs + CurrentLatencyMs)
                : _initialBufferMs;

            // Record when playback will actually start (now + buffer)
            _startTimeMs = Environment.TickCount64 + (long)initialBufferMs;

            // Start the stopwatch for measuring elapsed time
            _playbackTimer.Start();

            // Start the timer to update current time periodically
            _timeUpdateTimer.Change(_timeUpdateIntervalMs, _timeUpdateIntervalMs);
        }

        // Always send the data - the transport will handle chunking
        return Task.FromResult(true);
    }

    /// <summary>
    ///     Disposes resources used by the controller.
    /// </summary>
    public void Dispose()
    {
        _timeUpdateTimer.Dispose();
    }

    private event EventHandler<float>? TimeChangedInternal;

    // Private methods

    private void UpdateTime(object? state)
    {
        if (!_playbackTimer.IsRunning || _currentSampleRate <= 0)
        {
            return;
        }

        // Calculate current system time
        var currentTimeMs = Environment.TickCount64;

        // Calculate how much time has passed since playback started
        var elapsedSinceStartMs = currentTimeMs > _startTimeMs ? currentTimeMs - _startTimeMs : 0;

        // Apply latency adjustment if available
        var adjustedElapsedMs = HasLatencyInformation
            ? elapsedSinceStartMs - CurrentLatencyMs
            : elapsedSinceStartMs;

        // Don't allow negative time
        adjustedElapsedMs = Math.Max(0, adjustedElapsedMs);

        // Convert to seconds
        var newPlaybackTime = (float)(adjustedElapsedMs / 1000.0);

        // Only update and notify if the time changed significantly
        if (Math.Abs(newPlaybackTime - CurrentTime) >= 0.01f) // 10ms threshold
        {
            CurrentTime = newPlaybackTime;

            EventHandler<float>? handler;
            lock (_timeLock)
            {
                handler = TimeChangedInternal;
            }

            handler?.Invoke(this, newPlaybackTime);
        }
    }
}
