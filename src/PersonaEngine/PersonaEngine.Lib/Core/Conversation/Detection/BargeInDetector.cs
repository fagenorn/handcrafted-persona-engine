using System.Text;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PersonaEngine.Lib.Core.Conversation.Common.Messaging;
using PersonaEngine.Lib.Core.Conversation.Contracts.Events;

namespace PersonaEngine.Lib.Core.Conversation.Detection;

public sealed class BargeInDetector : IBargeInDetector
{
    private readonly ChannelWriter<BargeInDetected> _bargeInWriter;

    private readonly Lock _lock = new();

    private readonly ILogger<BargeInDetector> _logger;

    private readonly BargeInDetectorOptions _options;

    private readonly Dictionary<string, PotentialBargeInState> _potentialBargeIns = new();

    private readonly ChannelReader<object> _systemStateReader;

    private readonly ChannelReader<ITranscriptionEvent> _transcriptionEventReader;

    private Task? _executionTask;

    private bool _isAssistantSpeaking = false;

    private CancellationTokenSource? _serviceCts;

    public BargeInDetector(
        IChannelRegistry                        channelRegistry,
        ILogger<BargeInDetector>                logger,
        IOptionsMonitor<BargeInDetectorOptions> options)
    {
        _transcriptionEventReader = channelRegistry.TranscriptionEvents.Reader;
        _systemStateReader        = channelRegistry.SystemStateEvents.Reader;
        _bargeInWriter            = channelRegistry.BargeInEvents.Writer;
        _logger                   = logger ?? throw new ArgumentNullException(nameof(logger));
        _options                  = options.CurrentValue;

        _logger.LogInformation("BargeInDetector created. MinLength={MinLength}, Duration={Duration}ms",
                               _options.MinLength, _options.DetectionDuration.TotalMilliseconds);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting BargeInDetector...");
        _serviceCts    = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionTask = ProcessEventsAsync(_serviceCts.Token);
        _logger.LogInformation("BargeInDetector started.");

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if ( _serviceCts == null || _serviceCts.IsCancellationRequested || _executionTask == null )
        {
            return;
        }

        _logger.LogInformation("Stopping BargeInDetector...");
        await _serviceCts.CancelAsync();
        try
        {
            await _executionTask.WaitAsync(TimeSpan.FromSeconds(5));
            _logger.LogInformation("BargeInDetector execution task completed.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("BargeInDetector loop cancelled gracefully.");
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Timeout waiting for barge-in detector loop to stop.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during barge-in detector shutdown.");
        }
        finally
        {
            _bargeInWriter.TryComplete();
            _serviceCts?.Dispose();
            _serviceCts    = null;
            _executionTask = null;
            _logger.LogInformation("BargeInDetector stopped.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing BargeInDetector...");
        await StopAsync();
        _logger.LogInformation("BargeInDetector disposed.");
    }

    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting barge-in detection event loop.");

        var transcriptionTask = ReadChannelAsync(_transcriptionEventReader, cancellationToken);
        var systemStateTask   = ReadChannelAsync(_systemStateReader, cancellationToken);
        var tasks             = new List<Task> { transcriptionTask, systemStateTask };

        try
        {
            while ( !cancellationToken.IsCancellationRequested && tasks.Count > 0 )
            {
                var completedTask = await Task.WhenAny(tasks);

                if ( completedTask == transcriptionTask )
                {
                    var (transcriptionEvent, completed) = await transcriptionTask;
                    if ( transcriptionEvent is PotentialTranscriptUpdate potential )
                    {
                        HandlePotentialTranscript(potential);
                    }

                    if ( completed )
                    {
                        _logger.LogInformation("TranscriptionEvents channel completed for BargeInDetector.");
                        tasks.Remove(transcriptionTask);
                        transcriptionTask = null;
                    }
                    else
                    {
                        tasks.Remove(transcriptionTask);

                        transcriptionTask = ReadChannelAsync(_transcriptionEventReader, cancellationToken);
                        tasks.Add(transcriptionTask);
                    }
                }
                else if ( completedTask == systemStateTask )
                {
                    var (stateEvent, completed) = await systemStateTask;
                    HandleSystemStateEvent(stateEvent);

                    if ( completed )
                    {
                        _logger.LogInformation("SystemStateEvents channel completed for BargeInDetector.");
                        tasks.Remove(systemStateTask);
                        systemStateTask = null;
                    }
                    else
                    {
                        tasks.Remove(systemStateTask);

                        systemStateTask = ReadChannelAsync(_systemStateReader, cancellationToken);
                        tasks.Add(systemStateTask);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Barge-in detection loop cancelled via token.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in barge-in detection loop.");
            _bargeInWriter.TryComplete(ex);
        }
        finally
        {
            _logger.LogInformation("Barge-in detection event loop completed.");
            _bargeInWriter.TryComplete();
        }
    }

    private static async Task<(T? item, bool completed)> ReadChannelAsync<T>(ChannelReader<T> reader, CancellationToken ct) where T : class
    {
        try
        {
            if ( await reader.WaitToReadAsync(ct) )
            {
                if ( reader.TryRead(out var item) )
                {
                    return (item, false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            /* Expected */
        }
        catch (ChannelClosedException)
        {
            /* Expected */
        }

        return (null, true);
    }

    private void HandleSystemStateEvent(object? stateEvent)
    {
        lock (_lock)
        {
            switch ( stateEvent )
            {
                case AssistantSpeakingStarted:
                    _logger.LogDebug("Assistant speaking started. Enabling barge-in detection.");
                    _isAssistantSpeaking = true;
                    _potentialBargeIns.Clear();

                    break;
                case AssistantSpeakingStopped:
                    _logger.LogDebug("Assistant speaking stopped. Disabling barge-in detection.");
                    _isAssistantSpeaking = false;
                    _potentialBargeIns.Clear();

                    break;
            }
        }
    }

    private void HandlePotentialTranscript(PotentialTranscriptUpdate potential)
    {
        var                    shouldPublishBargeIn = false;
        PotentialBargeInState? detectedState        = null;

        lock (_lock)
        {
            if ( !_isAssistantSpeaking )
            {
                return;
            }

            if ( string.IsNullOrWhiteSpace(potential.PartialText) )
            {
                return;
            }

            if ( !_potentialBargeIns.TryGetValue(potential.SourceId, out var state) )
            {
                state                                  = new PotentialBargeInState(potential.Timestamp);
                _potentialBargeIns[potential.SourceId] = state;
                _logger.LogTrace("Potential barge-in started for SourceId: {SourceId}", potential.SourceId);
            }

            state.AppendText(potential.PartialText);
            state.LastUpdateTime = potential.Timestamp;

            var bargeInDuration = potential.Timestamp - state.StartTime;

            if ( bargeInDuration >= _options.DetectionDuration && state.DetectedText.Length >= _options.MinLength )
            {
                _logger.LogInformation("Barge-in criteria met: SourceId={SourceId}, Duration={Duration}ms, Length={Length}",
                                       potential.SourceId, bargeInDuration.TotalMilliseconds, state.DetectedText.Length);

                shouldPublishBargeIn = true;
                detectedState        = state;
                _potentialBargeIns.Remove(potential.SourceId);

                // Optional: Clear potential barge-ins from other sources too? Debatable.
                // _potentialBargeIns.Clear();
            }
        }

        if ( !shouldPublishBargeIn || detectedState == null )
        {
            return;
        }

        var bargeInEvent = new BargeInDetected(
                                               potential.SourceId,
                                               potential.Timestamp,
                                               detectedState.DetectedText);

        if ( !_bargeInWriter.TryWrite(bargeInEvent) )
        {
            _logger.LogWarning("Failed to write BargeInDetected event to channel (Channel full or closed).");
        }
    }

    private class PotentialBargeInState(DateTimeOffset startTime)
    {
        private readonly StringBuilder _textBuilder = new();

        public DateTimeOffset StartTime { get; } = startTime;

        public DateTimeOffset LastUpdateTime { get; set; } = startTime;

        public string DetectedText { get; private set; } = string.Empty;

        public void AppendText(string latestPartialText) { DetectedText = latestPartialText; }
    }
}