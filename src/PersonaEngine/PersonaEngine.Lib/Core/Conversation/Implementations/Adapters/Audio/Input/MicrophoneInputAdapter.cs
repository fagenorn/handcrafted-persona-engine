﻿using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using OpenAI.Chat;

using PersonaEngine.Lib.ASR.Transcriber;
using PersonaEngine.Lib.Audio;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Adapters.Audio.Input;

public sealed class MicrophoneInputAdapter(
    IMicrophone                     microphone,
    IRealtimeSpeechTranscriptor     speechTranscriptor,
    ILogger<MicrophoneInputAdapter> logger)
    : IAudioInputAdapter
{
    private ChannelWriter<IInputEvent>? _inputWriter;

    private bool _isDisposed;

    private Guid? _sessionId;

    private CancellationTokenSource? _transcriptionCts;

    private Task? _transcriptionTask;

    public Guid AdapterId { get; } = Guid.NewGuid();

    public ParticipantInfo Participant { get; } = new(Guid.NewGuid().ToString(), "User", ChatMessageRole.User);

    public IAwaitableAudioSource GetAudioSource() { return microphone; }

    public ValueTask InitializeAsync(Guid sessionId, ChannelWriter<IInputEvent> inputWriter, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if ( _transcriptionTask is not null && !_transcriptionTask.IsCompleted )
        {
            logger.LogWarning("Adapter is already running. Initialization skipped.");

            return ValueTask.CompletedTask;
        }

        _sessionId   = sessionId;
        _inputWriter = inputWriter ?? throw new ArgumentNullException(nameof(inputWriter));

        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if ( _inputWriter is null || _sessionId is null )
        {
            throw new InvalidOperationException("Adapter must be initialized before starting.");
        }

        if ( _transcriptionTask is not null && !_transcriptionTask.IsCompleted )
        {
            logger.LogInformation("Transcription task is already running.");

            return ValueTask.CompletedTask;
        }

        _transcriptionCts?.Dispose();
        _transcriptionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var localCt        = _transcriptionCts.Token;
        var localWriter    = _inputWriter;
        var localSessionId = _sessionId.Value;

        microphone.StartRecording();
        logger.LogInformation("Microphone recording started. Starting transcription task.");

        _transcriptionTask = Task.Run(async () => await TranscriptionJob(localSessionId, localWriter, localCt), localCt);

        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        if ( _isDisposed )
        {
            return;
        }

        logger.LogInformation("Stopping microphone recording and transcription task.");
        microphone.StopRecording();

        if ( _transcriptionCts is { IsCancellationRequested: false } )
        {
            await _transcriptionCts.CancelAsync();
        }

        if ( _transcriptionTask != null )
        {
            try
            {
                await _transcriptionTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Transcription task cancellation confirmed during stop.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception waiting for transcription task to complete during stop: {Message}", ex.Message);
            }
            finally
            {
                _transcriptionTask = null;
            }
        }

        _transcriptionCts?.Dispose();
        _transcriptionCts = null;
        _inputWriter      = null;
        _sessionId        = null;

        logger.LogInformation("Adapter stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        if ( _isDisposed )
        {
            return;
        }

        await StopAsync(CancellationToken.None);

        _transcriptionCts?.Dispose();

        _isDisposed = true;
    }

    private async ValueTask TranscriptionJob(Guid sessionId, ChannelWriter<IInputEvent> writer, CancellationToken ct)
    {
        logger.LogInformation("Transcription job starting for session {SessionId}.", sessionId);
        try
        {
            var eventsLoop = speechTranscriptor.TranscribeAsync(microphone, ct);

            await foreach ( var transcriptionEvent in eventsLoop )
            {
                if ( transcriptionEvent is not IRealtimeTranscriptionSegment transcriptionEventSeg )
                {
                    continue;
                }

                var inputEvent = MapToInputEvent(transcriptionEventSeg, sessionId);

                if ( inputEvent != null )
                {
                    await writer.WriteAsync(inputEvent, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Transcription job cancelled for session {SessionId}.", sessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error during transcription job for session {SessionId}: {Message}", sessionId, ex.Message);
        }
        finally
        {
            logger.LogInformation("Transcription job finished for session {SessionId}.", sessionId);
        }
    }

    private IInputEvent? MapToInputEvent(IRealtimeTranscriptionSegment transcriptionEvent, Guid sessionId)
    {
        return transcriptionEvent switch {
            RealtimeSegmentRecognized recognized => new SttSegmentRecognized(
                                                                             sessionId,
                                                                             DateTimeOffset.UtcNow,
                                                                             Participant.Id,
                                                                             recognized.Segment.Text,
                                                                             recognized.Segment.Duration,
                                                                             recognized.Segment.ConfidenceLevel),

            RealtimeSegmentRecognizing recognizing => new SttSegmentRecognizing(
                                                                                sessionId,
                                                                                DateTimeOffset.UtcNow,
                                                                                Participant.Id,
                                                                                recognizing.Segment.Text,
                                                                                recognizing.Segment.Duration),

            _ => null
        };
    }
}