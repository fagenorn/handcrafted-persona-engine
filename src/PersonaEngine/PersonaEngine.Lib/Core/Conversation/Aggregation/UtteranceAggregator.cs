using System.Text;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Core.Conversation.Common.Messaging;
using PersonaEngine.Lib.Core.Conversation.Contracts.Events;

namespace PersonaEngine.Lib.Core.Conversation.Aggregation;

public sealed class UtteranceAggregator : IUtteranceAggregator
{
    private static readonly TimeSpan DefaultSilenceTimeout = TimeSpan.FromSeconds(1.5);

    private readonly ILogger<UtteranceAggregator> _logger;

    private readonly TimeSpan _silenceTimeout;

    private readonly Lock _stateLock = new();

    private readonly ChannelReader<ITranscriptionEvent> _transcriptionEventReader;

    private readonly Dictionary<string, UtteranceBuilderState> _utteranceBuilders = new();

    private readonly ChannelWriter<UserUtteranceCompleted> _utteranceWriter;

    private Task? _executionTask;

    private CancellationTokenSource? _serviceCts;

    public UtteranceAggregator(
        IChannelRegistry             channelRegistry,
        ILogger<UtteranceAggregator> logger,
        TimeSpan?                    silenceTimeout = null)
    {
        _transcriptionEventReader = channelRegistry.TranscriptionEvents.Reader ?? throw new ArgumentNullException(nameof(channelRegistry));
        _utteranceWriter          = channelRegistry.UtteranceCompletionEvents.Writer ?? throw new ArgumentNullException(nameof(channelRegistry));
        _logger                   = logger ?? throw new ArgumentNullException(nameof(logger));
        _silenceTimeout           = silenceTimeout ?? DefaultSilenceTimeout;

        _logger.LogInformation("UtteranceAggregator created with SilenceTimeout: {Timeout}ms", _silenceTimeout.TotalMilliseconds);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting UtteranceAggregator...");
        _serviceCts    = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionTask = ProcessEventsAsync(_serviceCts.Token);
        _logger.LogInformation("UtteranceAggregator started.");

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if ( _serviceCts == null || _serviceCts.IsCancellationRequested || _executionTask == null )
        {
            _logger.LogWarning("StopAsync called but service is not running or already stopping.");

            return;
        }

        _logger.LogInformation("Stopping UtteranceAggregator...");
        await _serviceCts.CancelAsync();

        try
        {
            await _executionTask.WaitAsync(TimeSpan.FromSeconds(5));
            _logger.LogInformation("UtteranceAggregator execution task completed.");

            FinalizeAllUtterances("Service stopping");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("UtteranceAggregator loop cancelled gracefully.");
            FinalizeAllUtterances("Service cancelled");
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Timeout waiting for utterance aggregator loop to stop.");
            FinalizeAllUtterances("Service stop timeout");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during utterance aggregator shutdown.");
        }
        finally
        {
            _utteranceWriter.TryComplete();
            _serviceCts?.Dispose();
            _serviceCts    = null;
            _executionTask = null;
            _logger.LogInformation("UtteranceAggregator stopped.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing UtteranceAggregator...");
        await StopAsync();
        _logger.LogInformation("UtteranceAggregator disposed.");
    }

    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting utterance aggregation event loop.");
        try
        {
            await foreach ( var transcriptionEvent in _transcriptionEventReader.ReadAllAsync(cancellationToken) )
            {
                cancellationToken.ThrowIfCancellationRequested();

                if ( transcriptionEvent is FinalTranscriptSegmentReceived finalSegment )
                {
                    ProcessFinalSegment(finalSegment);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Utterance aggregation loop cancelled via token.");
        }
        catch (ChannelClosedException ex)
        {
            _logger.LogInformation("Transcription event channel closed, ending aggregation loop. Reason: {Reason}", ex.InnerException?.Message ?? "Channel completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in utterance aggregation loop.");
            _utteranceWriter.TryComplete(ex);
        }
        finally
        {
            _logger.LogInformation("Utterance aggregation event loop completed.");
            _utteranceWriter.TryComplete();

            lock (_stateLock)
            {
                foreach ( var builder in _utteranceBuilders.Values )
                {
                    builder.DisposeTimer();
                }

                _utteranceBuilders.Clear();
            }
        }
    }

    private void ProcessFinalSegment(FinalTranscriptSegmentReceived segment)
    {
        lock (_stateLock)
        {
            if ( !_utteranceBuilders.TryGetValue(segment.SourceId, out var builder) )
            {
                builder = new UtteranceBuilderState(segment, UtteranceTimedOut, _silenceTimeout, _logger);
                _utteranceBuilders.Add(segment.SourceId, builder);
                _logger.LogDebug("Started new utterance builder for SourceId: {SourceId}", segment.SourceId);
            }
            else
            {
                builder.AddSegment(segment);
                _logger.LogTrace("Appended segment to utterance for SourceId: {SourceId}", segment.SourceId);
            }

            builder.ResetTimer();
        }
    }

    private void UtteranceTimedOut(object? state)
    {
        if ( state is not string sourceId )
        {
            return;
        }

        _logger.LogDebug("Utterance timeout detected for SourceId: {SourceId}", sourceId);
        FinalizeUtterance(sourceId, "Timeout");
    }

    private void FinalizeUtterance(string sourceId, string reason)
    {
        UserUtteranceCompleted? completedEvent = null;
        lock (_stateLock)
        {
            if ( _utteranceBuilders.Remove(sourceId, out var builder) )
            {
                completedEvent = builder.CompleteUtterance();
                builder.DisposeTimer();
                _logger.LogInformation("Finalized utterance for SourceId: {SourceId}. Reason: {Reason}. Length: {Length}",
                                       sourceId, reason, completedEvent.AggregatedText.Length);
            }
            else
            {
                _logger.LogWarning("Attempted to finalize utterance for SourceId {SourceId}, but no builder was found (possibly already finalized). Reason: {Reason}", sourceId, reason);
            }
        }

        if ( completedEvent == null )
        {
            return;
        }

        // Use TryWrite, as writing might block if channel is full and downstream is slow.
        // Or use WriteAsync carefully if backpressure is desired.
        // Consider adding cancellation token if using WriteAsync.
        if ( !_utteranceWriter.TryWrite(completedEvent) )
        {
            _logger.LogWarning("Failed to write completed utterance to channel for SourceId: {SourceId} (Channel full or closed).", sourceId);
        }
    }

    private void FinalizeAllUtterances(string reason)
    {
        _logger.LogInformation("Finalizing all pending utterances. Reason: {Reason}", reason);
        List<string> sourceIds;
        lock (_stateLock)
        {
            sourceIds = new List<string>(_utteranceBuilders.Keys);
        }

        foreach ( var sourceId in sourceIds )
        {
            FinalizeUtterance(sourceId, reason);
        }

        _logger.LogInformation("Finished finalizing all pending utterances.");
    }

    private class UtteranceBuilderState
    {
        private readonly ILogger _logger;

        private readonly List<FinalTranscriptSegmentReceived> _segments = new();

        private readonly StringBuilder _textBuilder = new();

        private readonly TimeSpan _timeoutDuration;

        private Timer? _timer;

        public UtteranceBuilderState(FinalTranscriptSegmentReceived firstSegment, TimerCallback timeoutCallback, TimeSpan timeoutDuration, ILogger logger)
        {
            SourceId             = firstSegment.SourceId;
            User                 = firstSegment.User;
            StartTimestamp       = firstSegment.Timestamp;
            LastSegmentTimestamp = firstSegment.Timestamp;
            _timeoutDuration     = timeoutDuration;
            _logger              = logger;

            _segments.Add(firstSegment);
            _textBuilder.Append(firstSegment.Text).Append(" ");

            // Initialize timer but don't start it immediately? Or start now? Let's start now.
            _timer = new Timer(timeoutCallback, SourceId, _timeoutDuration, Timeout.InfiniteTimeSpan);
        }

        public string User { get; private set; }

        public DateTimeOffset StartTimestamp { get; }

        public DateTimeOffset LastSegmentTimestamp { get; private set; }

        public string SourceId { get; }

        public void AddSegment(FinalTranscriptSegmentReceived segment)
        {
            // Update user if it changes (take the latest?) - depends on desired logic
            // User = segment.User;
            LastSegmentTimestamp = segment.Timestamp;
            _segments.Add(segment);
            _textBuilder.Append(segment.Text).Append(" ");
        }

        public void ResetTimer()
        {
            _timer?.Change(_timeoutDuration, Timeout.InfiniteTimeSpan);
            _logger.LogTrace("Reset utterance timer for SourceId: {SourceId}", SourceId);
        }

        public void DisposeTimer()
        {
            _timer?.Dispose();
            _timer = null;
        }

        public UserUtteranceCompleted CompleteUtterance()
        {
            return new UserUtteranceCompleted(
                                              _textBuilder.ToString().Trim(),
                                              SourceId,
                                              User,
                                              StartTimestamp,
                                              LastSegmentTimestamp,
                                              _segments.AsReadOnly()
                                             );
        }
    }
}