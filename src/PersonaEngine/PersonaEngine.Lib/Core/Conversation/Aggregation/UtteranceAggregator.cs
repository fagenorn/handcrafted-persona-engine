// ========================================================================
// FILE: src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/Aggregation/UtteranceAggregator.cs
// REASON: Simplified logic to rely solely on IRealtimeSpeechTranscriptor's
//         segmentation (VAD). Removed internal silence timer and buffering.
//         Each FinalTranscriptSegmentReceived is now treated as a complete utterance.
// ========================================================================
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Core.Conversation.Common.Messaging;
using PersonaEngine.Lib.Core.Conversation.Contracts.Events;

namespace PersonaEngine.Lib.Core.Conversation.Aggregation;

/// <summary>
///     Service responsible for converting final transcript segments directly into completed utterances,
///     relying on the upstream transcription service (IRealtimeSpeechTranscriptor) for segmentation via VAD.
/// </summary>
public sealed class UtteranceAggregator : IUtteranceAggregator
{
    private readonly ILogger<UtteranceAggregator> _logger;

    private readonly ChannelReader<ITranscriptionEvent> _transcriptionEventReader;

    private readonly ChannelWriter<UserUtteranceCompleted> _utteranceWriter;

    private Task? _executionTask;

    private CancellationTokenSource? _serviceCts;

    public UtteranceAggregator(
        IChannelRegistry             channelRegistry,
        ILogger<UtteranceAggregator> logger)
    {
        // Removed silenceTimeout parameter as it's no longer used
        _transcriptionEventReader = channelRegistry.TranscriptionEvents.Reader ?? throw new ArgumentNullException(nameof(channelRegistry.TranscriptionEvents));
        _utteranceWriter          = channelRegistry.UtteranceCompletionEvents.Writer ?? throw new ArgumentNullException(nameof(channelRegistry.UtteranceCompletionEvents));
        _logger                   = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("UtteranceAggregator created (Simplified: Relies on upstream VAD/segmentation).");
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
        // Use CancelAsync for potentially cleaner cancellation if available
        try
        {
            await _serviceCts.CancelAsync();
        }
        catch
        {
            _serviceCts.Cancel();
        }

        try
        {
            await _executionTask.WaitAsync(TimeSpan.FromSeconds(5));
            _logger.LogInformation("UtteranceAggregator execution task completed.");
            // No utterances to finalize anymore
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("UtteranceAggregator loop cancelled gracefully.");
            // No utterances to finalize anymore
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Timeout waiting for utterance aggregator loop to stop.");
            // No utterances to finalize anymore
        }
        catch (Exception ex) when (_executionTask.IsFaulted) // Catch exceptions from the task itself
        {
            _logger.LogError(ex, "Error during utterance aggregator execution: {ExceptionMessage}", _executionTask.Exception?.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during utterance aggregator shutdown.");
        }
        finally
        {
            _utteranceWriter.TryComplete(); // Ensure writer is completed
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

    /// <summary>
    ///     Processes transcription events, converting each final segment into a completed utterance.
    /// </summary>
    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting simplified utterance aggregation event loop.");
        try
        {
            // Read all transcription events
            await foreach ( var transcriptionEvent in _transcriptionEventReader.ReadAllAsync(cancellationToken) )
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Only process final, non-empty segments
                if ( transcriptionEvent is FinalTranscriptSegmentReceived finalSegment &&
                     !string.IsNullOrWhiteSpace(finalSegment.Text) )
                {
                    _logger.LogTrace("Processing FinalTranscriptSegmentReceived for SourceId: {SourceId}", finalSegment.SourceId);

                    // Create UserUtteranceCompleted directly from the final segment
                    var completedEvent = new UserUtteranceCompleted(
                                                                    finalSegment.Text.Trim(), // Use segment text directly
                                                                    finalSegment.SourceId,
                                                                    finalSegment.User,      // Assuming User is populated by TranscriptionService
                                                                    finalSegment.Timestamp, // Start and End are the same
                                                                    finalSegment.Timestamp,
                                                                    new List<FinalTranscriptSegmentReceived> { finalSegment } // Include the single segment
                                                                   );

                    _logger.LogInformation("Publishing UserUtteranceCompleted from final segment. SourceId: {SourceId}, Length: {Length}",
                                           completedEvent.SourceId, completedEvent.AggregatedText.Length);

                    // Use TryWrite to avoid blocking if the downstream channel is full
                    if ( !_utteranceWriter.TryWrite(completedEvent) )
                    {
                        _logger.LogWarning("Failed to write completed utterance to channel for SourceId: {SourceId} (Channel full or closed).", finalSegment.SourceId);
                        // Depending on requirements, could implement retry or drop logic here
                    }
                }
                else if ( transcriptionEvent is PotentialTranscriptUpdate potentialUpdate )
                {
                    // Ignore potential updates in this simplified model
                    _logger.LogTrace("Ignoring PotentialTranscriptUpdate for SourceId: {SourceId}", potentialUpdate.SourceId);
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
            _utteranceWriter.TryComplete(ex); // Signal error downstream
        }
        finally
        {
            _logger.LogInformation("Utterance aggregation event loop completed.");
            _utteranceWriter.TryComplete(); // Ensure completion is signaled
            // No builders or timers to clean up
        }
    }
}