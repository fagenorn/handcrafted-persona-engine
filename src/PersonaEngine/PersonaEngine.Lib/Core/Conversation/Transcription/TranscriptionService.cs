using System.Threading.Channels;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PersonaEngine.Lib.ASR.Transcriber;
using PersonaEngine.Lib.Audio;
using PersonaEngine.Lib.Core.Conversation.Adapters;
using PersonaEngine.Lib.Core.Conversation.Common.Messaging;
using PersonaEngine.Lib.Core.Conversation.Contracts.Events;
using PersonaEngine.Lib.UI.Common;

using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.Core.Conversation.Transcription;

public sealed class TranscriptionService : IInputAdapter, IStartupTask
{
    private readonly ChannelWriter<ITranscriptionEvent> _eventWriter;

    private readonly ILogger<TranscriptionService> _logger;

    private readonly IMicrophone _microphone;

    private readonly TranscriptionServiceOptions _options;

    private readonly string _sourceId;

    private readonly IRealtimeSpeechTranscriptor _transcriptor;

    private Task? _executionTask;

    private CancellationTokenSource? _serviceCts;

    public TranscriptionService(
        IMicrophone                           microphone,
        IRealtimeSpeechTranscriptor           transcriptor,
        IChannelRegistry                      channelRegistry,
        ILogger<TranscriptionService>         logger,
        IOptions<TranscriptionServiceOptions> options)
    {
        _microphone   = microphone;
        _transcriptor = transcriptor;
        _eventWriter  = channelRegistry.TranscriptionEvents.Writer;
        _logger       = logger;
        _options      = options.Value;

        if ( string.IsNullOrWhiteSpace(_options.SourceId) )
        {
            throw new ArgumentException("SourceId cannot be null or whitespace in TranscriptionServiceOptions.", nameof(options));
        }

        SourceId = _options.SourceId;

        _logger.LogInformation("TranscriptionService created for SourceId: {SourceId}", _sourceId);
    }

    public string SourceId { get; }

    /// <summary>
    ///     Starts the transcription loop in the background.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting TranscriptionService (IInputAdapter) for SourceId: {SourceId}...", SourceId);
        _serviceCts    = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionTask = RunTranscriptionLoopAsync(_serviceCts.Token);
        _logger.LogInformation("TranscriptionService (IInputAdapter) started for SourceId: {SourceId}.", SourceId);

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Stops the transcription loop gracefully.
    /// </summary>
    public async Task StopAsync()
    {
        if ( _serviceCts == null || _serviceCts.IsCancellationRequested || _executionTask == null )
        {
            _logger.LogWarning("StopAsync called on TranscriptionService {SourceId} but service is not running or already stopping.", SourceId);

            return;
        }

        _logger.LogInformation("Stopping TranscriptionService (IInputAdapter) for SourceId: {SourceId}...", SourceId);
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
            
            _logger.LogInformation("TranscriptionService (IInputAdapter) stopped for SourceId: {SourceId}.", SourceId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing TranscriptionService (IInputAdapter) for SourceId: {SourceId}...", SourceId);
        await StopAsync();
        _logger.LogInformation("TranscriptionService (IInputAdapter) disposed for SourceId: {SourceId}.", SourceId);
        _serviceCts?.Dispose();
    }

    private async Task RunTranscriptionLoopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting transcription loop for SourceId: {SourceId}", SourceId);

        try
        {
            _microphone.StartRecording();

            await foreach ( var recognitionEvent in _transcriptor.TranscribeAsync(_microphone, cancellationToken) )
            {
                cancellationToken.ThrowIfCancellationRequested();

                ITranscriptionEvent? transcriptionEvent = null;

                switch ( recognitionEvent )
                {
                    case RealtimeSegmentRecognized recognized:

                        if ( !recognized.Segment.Metadata.TryGetValue("User", out var user) )
                        {
                            user = "UnknownUser";
                        }

                        transcriptionEvent = new FinalTranscriptSegmentReceived(
                                                                                recognized.Segment.Text,
                                                                                SourceId,
                                                                                user,
                                                                                DateTimeOffset.Now);

                        _logger.LogTrace("[{SourceId}] Final segment: User='{User}', Text='{Text}'", SourceId, user, recognized.Segment.Text);
                        
                        break;

                    case RealtimeSegmentRecognizing recognizing:
                        if ( !string.IsNullOrWhiteSpace(recognizing.Segment.Text) )
                        {
                            transcriptionEvent = new PotentialTranscriptUpdate(
                                                                               recognizing.Segment.Text,
                                                                               SourceId,
                                                                               DateTimeOffset.Now);

                            _logger.LogTrace("[{SourceId}] Potential segment: '{Text}'", SourceId, recognizing.Segment.Text);
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
                    _logger.LogWarning("[{SourceId}] Transcription event channel closed while trying to write. Exiting loop.", SourceId);

                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Transcription loop cancelled via token for SourceId: {SourceId}.", SourceId);
        }
        catch (ChannelClosedException)
        {
            _logger.LogInformation("Transcription channel closed, ending loop for SourceId: {SourceId}.", SourceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in transcription loop for SourceId: {SourceId}", SourceId);
            _eventWriter.TryComplete(ex);
        }
        finally
        {
            _microphone.StopRecording();
            // Signal that no more items will be written.
            // This is crucial for consumers reading with ReadAllAsync.
            _eventWriter.TryComplete();
            _logger.LogInformation("Transcription loop completed for SourceId: {SourceId}.", SourceId);
        }
    }

    public void Execute(GL gl)
    {
        StartAsync(CancellationToken.None);
    }
}