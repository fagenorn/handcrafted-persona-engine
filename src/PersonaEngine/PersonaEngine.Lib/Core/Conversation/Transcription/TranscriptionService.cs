using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.ASR.Transcriber;
using PersonaEngine.Lib.Audio;
using PersonaEngine.Lib.Core.Conversation.Common.Messaging;
using PersonaEngine.Lib.Core.Conversation.Contracts.Events;

namespace PersonaEngine.Lib.Core.Conversation.Transcription;

public sealed class TranscriptionService : ITranscriptionService
{
    private readonly ChannelWriter<ITranscriptionEvent> _eventWriter;

    private readonly ILogger<TranscriptionService> _logger;

    private readonly IMicrophone _microphone;

    private readonly string _sourceId;

    private readonly IRealtimeSpeechTranscriptor _transcriptor;

    private Task? _executionTask;

    private CancellationTokenSource? _serviceCts;

    public TranscriptionService(
        IMicrophone                   microphone,
        IRealtimeSpeechTranscriptor   transcriptor,
        IChannelRegistry              channelRegistry,
        ILogger<TranscriptionService> logger,
        string                        sourceId = "DefaultMicrophone")
    {
        _microphone   = microphone ?? throw new ArgumentNullException(nameof(microphone));
        _transcriptor = transcriptor ?? throw new ArgumentNullException(nameof(transcriptor));
        _eventWriter  = channelRegistry?.TranscriptionEvents.Writer ?? throw new ArgumentNullException(nameof(channelRegistry));
        _logger       = logger ?? throw new ArgumentNullException(nameof(logger));
        _sourceId     = sourceId;

        _logger.LogInformation("TranscriptionService created for SourceId: {SourceId}", _sourceId);
    }

    /// <summary>
    ///     Starts the transcription loop in the background.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting TranscriptionService for SourceId: {SourceId}...", _sourceId);
        _serviceCts    = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionTask = RunTranscriptionLoopAsync(_serviceCts.Token);
        _logger.LogInformation("TranscriptionService started for SourceId: {SourceId}.", _sourceId);

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Stops the transcription loop gracefully.
    /// </summary>
    public async Task StopAsync()
    {
        if ( _serviceCts == null || _serviceCts.IsCancellationRequested || _executionTask == null )
        {
            _logger.LogWarning("StopAsync called but service is not running or already stopping.");

            return;
        }

        _logger.LogInformation("Stopping TranscriptionService for SourceId: {SourceId}...", _sourceId);
        await _serviceCts.CancelAsync();

        try
        {
            await _executionTask.WaitAsync(TimeSpan.FromSeconds(5));
            _logger.LogInformation("TranscriptionService execution task completed for SourceId: {SourceId}.", _sourceId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Transcription loop cancelled gracefully for SourceId: {SourceId}.", _sourceId);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Timeout waiting for transcription loop to stop for SourceId: {SourceId}.", _sourceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during transcription loop shutdown for SourceId: {SourceId}.", _sourceId);
        }
        finally
        {
            _eventWriter.TryComplete();
            _serviceCts.Dispose();
            _serviceCts    = null;
            _executionTask = null;
            _logger.LogInformation("TranscriptionService stopped for SourceId: {SourceId}.", _sourceId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing TranscriptionService for SourceId: {SourceId}...", _sourceId);
        await StopAsync();
        _logger.LogInformation("TranscriptionService disposed for SourceId: {SourceId}.", _sourceId);
        _serviceCts?.Dispose();
    }

    private async Task RunTranscriptionLoopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting transcription loop for SourceId: {SourceId}", _sourceId);

        try
        {
            _microphone.StartRecording();

            await foreach ( var event_ in _transcriptor.TranscribeAsync(_microphone, cancellationToken) )
            {
                cancellationToken.ThrowIfCancellationRequested();

                ITranscriptionEvent? transcriptionEvent = null;

                switch ( event_ )
                {
                    case RealtimeSegmentRecognized recognized:

                        if ( !recognized.Segment.Metadata.TryGetValue("User", out var user) )
                        {
                            user = "UnknownUser";
                        }

                        transcriptionEvent = new FinalTranscriptSegmentReceived(
                                                                                recognized.Segment.Text,
                                                                                _sourceId,
                                                                                user,
                                                                                DateTimeOffset.Now);

                        _logger.LogTrace("[{SourceId}] Final segment: User='{User}', Text='{Text}'", _sourceId, user, recognized.Segment.Text);

                        break;

                    case RealtimeSegmentRecognizing recognizing:
                        if ( !string.IsNullOrWhiteSpace(recognizing.Segment.Text) )
                        {
                            transcriptionEvent = new PotentialTranscriptUpdate(
                                                                               recognizing.Segment.Text,
                                                                               _sourceId,
                                                                               DateTimeOffset.Now);

                            _logger.LogTrace("[{SourceId}] Potential segment: '{Text}'", _sourceId, recognizing.Segment.Text);
                        }

                        break;
                }

                if ( transcriptionEvent == null )
                {
                    continue;
                }

                try
                {
                    await _eventWriter.WriteAsync(transcriptionEvent, cancellationToken);
                }
                catch (ChannelClosedException)
                {
                    _logger.LogWarning("[{SourceId}] Transcription event channel closed while trying to write. Exiting loop.", _sourceId);

                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Transcription loop cancelled via token for SourceId: {SourceId}.", _sourceId);
        }
        catch (ChannelClosedException)
        {
            _logger.LogInformation("Transcription channel closed, ending loop for SourceId: {SourceId}.", _sourceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in transcription loop for SourceId: {SourceId}", _sourceId);
            _eventWriter.TryComplete(ex);
        }
        finally
        {
            _microphone.StopRecording();
            // Signal that no more items will be written.
            // This is crucial for consumers reading with ReadAllAsync.
            _eventWriter.TryComplete();
            _logger.LogInformation("Transcription loop completed for SourceId: {SourceId}.", _sourceId);
        }
    }
}