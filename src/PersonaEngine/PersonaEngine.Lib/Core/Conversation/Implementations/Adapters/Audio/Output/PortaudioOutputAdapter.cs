using System.Runtime.InteropServices;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Audio.Player;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;
using PersonaEngine.Lib.TTS.Synthesis;

using PortAudioSharp;

using Stream = PortAudioSharp.Stream;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Adapters.Audio.Output;

public class PortaudioOutputAdapter(ILogger<PortaudioOutputAdapter> logger, IAudioProgressNotifier progressNotifier) : IAudioOutputAdapter
{
    private const int DefaultSampleRate = 24000;

    private const int DefaultFrameBufferSize = 1024;

    private readonly float[] _frameBuffer = new float[DefaultFrameBufferSize];

    private Stream? _audioStream;

    private AudioBuffer? _currentBuffer;

    private ChannelReader<TtsChunkEvent>? _currentReader;

    private Guid _currentTurnId;

    private long _framesOfChunkPlayed;

    private TaskCompletionSource<bool>? _playbackCompletion;

    private CancellationTokenSource? _playbackCts;

    private volatile bool _producerCompleted;

    private Channel<IAudioProgressEvent>? _progressChannel;

    private Guid _sessionId;

    public ValueTask DisposeAsync()
    {
        try
        {
            _audioStream = null;
        }
        catch
        {
            /* ignored */
        }

        return ValueTask.CompletedTask;
    }

    public Guid AdapterId { get; } = Guid.NewGuid();

    public ValueTask InitializeAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        _sessionId = sessionId;

        try
        {
            PortAudio.Initialize();
            var deviceIndex = PortAudio.DefaultOutputDevice;
            if ( deviceIndex == PortAudio.NoDevice )
            {
                throw new AudioDeviceNotFoundException("No default PortAudio output device found.");
            }

            var deviceInfo = PortAudio.GetDeviceInfo(deviceIndex);
            logger.LogDebug("Using PortAudio output device: {DeviceName} (Index: {DeviceIndex})", deviceInfo.name, deviceIndex);

            var parameters = new StreamParameters {
                                                      device                    = deviceIndex,
                                                      channelCount              = 1,
                                                      sampleFormat              = SampleFormat.Float32,
                                                      suggestedLatency          = deviceInfo.defaultLowOutputLatency,
                                                      hostApiSpecificStreamInfo = IntPtr.Zero
                                                  };

            _audioStream = new Stream(
                                      null,
                                      parameters,
                                      DefaultSampleRate,
                                      DefaultFrameBufferSize,
                                      StreamFlags.ClipOff,
                                      AudioCallback,
                                      IntPtr.Zero);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize PortAudio or create stream.");

            try
            {
                PortAudio.Terminate();
            }
            catch
            {
                // ignored
            }

            throw new AudioPlayerInitializationException("Failed to initialize PortAudio audio system.", ex);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        // Don't start it here, SendAsync will handle it.

        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        // Don't stop it here, SendAsync's cleanup or cancellation will handle it.
        // If a hard stop independent of SendAsync is needed, cancellation of SendAsync's token is the way.

        return ValueTask.CompletedTask;
    }

    public async Task SendAsync(ChannelReader<TtsChunkEvent> inputReader, ChannelWriter<IOutputEvent> outputWriter, Guid turnId, CancellationToken cancellationToken = default)
    {
        if ( _audioStream == null )
        {
            throw new InvalidOperationException("Audio stream is not initialized.");
        }

        _currentTurnId = turnId;
        _currentReader = inputReader;

        _progressChannel = Channel.CreateBounded<IAudioProgressEvent>(new BoundedChannelOptions(10) { SingleReader = true, SingleWriter = true });

        _playbackCts        = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _playbackCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var       completedReason  = CompletionReason.Completed;
        var       firstChunk       = true;
        var       localPlaybackCts = _playbackCts;
        var       localCompletion  = _playbackCompletion;
        using var eventLoopCts     = new CancellationTokenSource();
        var       progressPumpTask = ProgressEventsLoop(eventLoopCts.Token);

        try
        {
            _audioStream.Start();

            while ( await inputReader.WaitToReadAsync(localPlaybackCts.Token).ConfigureAwait(false) )
            {
                if ( !firstChunk )
                {
                    continue;
                }

                var firstChunkEvent = new AudioPlaybackStartedEvent(_sessionId, turnId, DateTimeOffset.UtcNow);
                await outputWriter.WriteAsync(firstChunkEvent, localPlaybackCts.Token).ConfigureAwait(false);

                firstChunk = false;
            }

            _producerCompleted = true;

            await localCompletion.Task.ConfigureAwait(false);
            await progressPumpTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            completedReason = CompletionReason.Cancelled;

            eventLoopCts.CancelAfter(250);

            await localCompletion.Task.ConfigureAwait(false);
            await progressPumpTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            completedReason = CompletionReason.Error;

            await outputWriter.WriteAsync(new ErrorOutputEvent(_sessionId, turnId, DateTimeOffset.UtcNow, ex), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            CleanupPlayback();

            if ( !firstChunk )
            {
                await outputWriter.WriteAsync(new AudioPlaybackEndedEvent(_sessionId, turnId, DateTimeOffset.UtcNow, completedReason), CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask ProgressEventsLoop(CancellationToken ct = default)
    {
        var eventsReader = _progressChannel!.Reader;

        await foreach ( var progressEvent in eventsReader.ReadAllAsync(ct).ConfigureAwait(false) )
        {
            if ( ct.IsCancellationRequested )
            {
                break;
            }

            switch ( progressEvent )
            {
                case AudioChunkPlaybackStartedEvent startedArgs:
                    progressNotifier.RaiseChunkStarted(this, startedArgs);

                    break;
                case AudioChunkPlaybackEndedEvent endedArgs:
                    progressNotifier.RaiseChunkEnded(this, endedArgs);

                    break;
                case AudioPlaybackProgressEvent progressArgs:
                    progressNotifier.RaiseProgress(this, progressArgs);

                    break;
            }
        }
    }

    private void CleanupPlayback()
    {
        _audioStream?.Stop();

        _currentTurnId = Guid.Empty;
        _currentReader = null;

        _progressChannel?.Writer.TryComplete();
        _producerCompleted   = false;
        _framesOfChunkPlayed = 0;
        _currentBuffer       = null;

        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _playbackCts = null;

        Array.Clear(_frameBuffer, 0, DefaultFrameBufferSize);
    }

    private StreamCallbackResult AudioCallback(IntPtr input, IntPtr output, uint framecount, ref StreamCallbackTimeInfo timeinfo, StreamCallbackFlags statusflags, IntPtr userdataptr)
    {
        var framesRequested = (int)framecount;
        var framesWritten   = 0;

        var turnId             = _currentTurnId;
        var reader             = _currentReader;
        var completionSource   = _playbackCompletion;
        var currentPlaybackCts = _playbackCts;
        var ct                 = currentPlaybackCts?.Token ?? CancellationToken.None;
        var progressWriter     = _progressChannel!.Writer;

        if ( reader == null )
        {
            completionSource?.TrySetResult(false);
            progressWriter.TryComplete();

            return StreamCallbackResult.Complete;
        }

        while ( framesWritten < framesRequested )
        {
            if ( ct.IsCancellationRequested )
            {
                FillSilence();
                completionSource?.TrySetResult(false);

                if ( _currentBuffer != null )
                {
                    progressWriter.TryWrite(new AudioChunkPlaybackEndedEvent(_sessionId, turnId, DateTimeOffset.UtcNow, _currentBuffer.Segment));
                }

                progressWriter.TryComplete();

                return StreamCallbackResult.Complete;
            }

            if ( _currentBuffer == null )
            {
                if ( reader.TryRead(out var chunk) )
                {
                    _currentBuffer = new AudioBuffer(chunk.Chunk.AudioData, chunk.Chunk);
                    progressWriter.TryWrite(new AudioChunkPlaybackStartedEvent(_sessionId, turnId, DateTimeOffset.UtcNow, chunk.Chunk));
                }
                else
                {
                    if ( _producerCompleted )
                    {
                        FillSilence();
                        completionSource?.TrySetResult(true);
                        progressWriter.TryComplete();

                        return StreamCallbackResult.Complete;
                    }

                    FillSilence();
                }
            }

            if ( _currentBuffer == null )
            {
                return StreamCallbackResult.Continue;
            }

            var framesToCopy    = Math.Min(framesRequested - framesWritten, _currentBuffer.Remaining);
            var sourceSpan      = _currentBuffer.Data.Span.Slice(_currentBuffer.Position, framesToCopy);
            var destinationSpan = _frameBuffer.AsSpan(framesWritten, framesToCopy);
            sourceSpan.CopyTo(destinationSpan);
            _currentBuffer.Advance(framesToCopy);
            framesWritten += framesToCopy;

            if ( _currentBuffer.IsFinished )
            {
                progressWriter.TryWrite(new AudioChunkPlaybackEndedEvent(_sessionId, turnId, DateTimeOffset.UtcNow, _currentBuffer.Segment));
                _framesOfChunkPlayed = 0;
                _currentBuffer       = null;
            }
        }

        Marshal.Copy(_frameBuffer, 0, output, framesRequested);

        _framesOfChunkPlayed += framesRequested;
        var currentTime = TimeSpan.FromSeconds((double)_framesOfChunkPlayed / DefaultSampleRate);
        progressWriter.TryWrite(new AudioPlaybackProgressEvent(_sessionId, turnId, DateTimeOffset.UtcNow, currentTime));

        if ( _currentBuffer == null && _producerCompleted )
        {
            completionSource?.TrySetResult(true);
            if ( _currentBuffer != null )
            {
                progressWriter.TryWrite(new AudioChunkPlaybackEndedEvent(_sessionId, turnId, DateTimeOffset.UtcNow, _currentBuffer.Segment));
            }

            progressWriter.TryComplete();

            return StreamCallbackResult.Complete;
        }

        if ( ct.IsCancellationRequested )
        {
            completionSource?.TrySetResult(false);
            if ( _currentBuffer != null )
            {
                progressWriter.TryWrite(new AudioChunkPlaybackEndedEvent(_sessionId, turnId, DateTimeOffset.UtcNow, _currentBuffer.Segment));
            }

            progressWriter.TryComplete();

            return StreamCallbackResult.Complete;
        }

        return StreamCallbackResult.Continue;

        void FillSilence()
        {
            if ( framesWritten < framesRequested )
            {
                Array.Clear(_frameBuffer, framesWritten, framesRequested - framesWritten);
            }

            Marshal.Copy(_frameBuffer, 0, output, framesRequested);
        }
    }

    private sealed class AudioBuffer(Memory<float> data, AudioSegment segment)
    {
        public Memory<float> Data { get; } = data;

        public AudioSegment Segment { get; } = segment;

        public int Position { get; private set; } = 0;

        public int Remaining => Data.Length - Position;

        public bool IsFinished => Position >= Data.Length;

        public void Advance(int count) { Position += count; }
    }
}