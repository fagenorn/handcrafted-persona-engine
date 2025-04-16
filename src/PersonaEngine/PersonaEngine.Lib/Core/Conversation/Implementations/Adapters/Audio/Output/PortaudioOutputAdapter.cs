using System.Runtime.InteropServices;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Audio.Player;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Input;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;

using PortAudioSharp;

using Stream = PortAudioSharp.Stream;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Adapters.Audio.Output;

public class PortaudioOutputAdapter(ILogger<PortaudioOutputAdapter> logger) : IAudioOutputAdapter
{
    private const int DefaultSampleRate = 24000;

    private const int DefaultFrameBufferSize = 1024;

    private readonly float[] _frameBuffer = new float[DefaultFrameBufferSize];

    private Stream? _audioStream;

    private AudioBuffer? _currentBuffer;

    private ChannelReader<TtsChunkEvent>? _currentReader;

    private TaskCompletionSource<bool>? _playbackCompletion;

    private CancellationTokenSource? _playbackCts;

    private volatile bool _producerCompleted;

    private Guid _sessionId;

    public ValueTask DisposeAsync()
    {
        Console.WriteLine("PortaudioOutputAdapter: DisposeAsync called");

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
        if ( _audioStream == null )
        {
            throw new InvalidOperationException("Audio stream is not initialized.");
        }

        if ( _audioStream.IsActive )
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            // _audioStream.Start();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start PortAudio stream.");

            throw new AudioException("Failed to start audio playback stream.", ex);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        if ( _audioStream is not { IsActive: true } )
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            // _audioStream.Stop();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error stopping PortAudio stream.");

            throw new AudioException("Failed to stop audio playback stream.", ex);
        }

        return ValueTask.CompletedTask;
    }

    public async Task SendAsync(ChannelReader<TtsChunkEvent> inputReader, ChannelWriter<IOutputEvent> outputWriter, Guid turnId, CancellationToken cancellationToken = default)
    {
        var completedReason = CompletionReason.Completed;
        var firstChunk      = true;

        if ( _audioStream == null )
        {
            throw new InvalidOperationException("Audio stream is not initialized.");
        }

        try
        {
            _producerCompleted  = false;
            _playbackCts        = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _playbackCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _currentReader      = inputReader;

            _audioStream.Start();

            while ( await inputReader.WaitToReadAsync(cancellationToken).ConfigureAwait(false) )
            {
                if ( !firstChunk )
                {
                    continue;
                }

                var firstChunkEvent = new AudioPlaybackStartedEvent(_sessionId, turnId, DateTimeOffset.UtcNow);
                await outputWriter.WriteAsync(firstChunkEvent, cancellationToken).ConfigureAwait(false);

                firstChunk = false;
            }

            _producerCompleted = true;

            await _playbackCompletion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            completedReason = CompletionReason.Cancelled;
        }
        catch (Exception ex)
        {
            completedReason = CompletionReason.Error;

            await outputWriter.WriteAsync(new ErrorOutputEvent(_sessionId, turnId, DateTimeOffset.UtcNow, ex), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CleanupPlayback();

            if ( !firstChunk )
            {
                await outputWriter.WriteAsync(new AudioPlaybackEndedEvent(_sessionId, turnId, DateTimeOffset.UtcNow, completedReason), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void CleanupPlayback()
    {
        _audioStream?.Stop();

        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _playbackCts = null;

        _currentReader     = null;
        _currentBuffer     = null;
        _producerCompleted = false;

        Array.Clear(_frameBuffer, 0, DefaultFrameBufferSize);
    }

    private StreamCallbackResult AudioCallback(IntPtr input, IntPtr output, uint framecount, ref StreamCallbackTimeInfo timeinfo, StreamCallbackFlags statusflags, IntPtr userdataptr)
    {
        var framesRequested           = (int)framecount;
        var currentReader             = _currentReader;
        var currentPlaybackCts        = _playbackCts;
        var currentPlaybackCompletion = _playbackCompletion;
        var ct                        = currentPlaybackCts?.Token ?? CancellationToken.None;

        if ( currentReader == null )
        {
            currentPlaybackCompletion?.TrySetResult(false);

            return StreamCallbackResult.Complete;
        }

        var framesWritten = 0;
        while ( framesWritten < framesRequested )
        {
            if ( ct.IsCancellationRequested )
            {
                currentPlaybackCompletion?.TrySetResult(false);

                return StreamCallbackResult.Complete;
            }

            if ( _currentBuffer == null )
            {
                if ( !currentReader.TryRead(out var chunk) )
                {
                    if ( _producerCompleted )
                    {
                        currentPlaybackCompletion?.TrySetResult(true);
                        FillSilence();

                        return StreamCallbackResult.Complete;
                    }

                    FillSilence();
                }
                else
                {
                    _currentBuffer = new AudioBuffer(chunk.Chunk.AudioData);
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
                _currentBuffer = null;
            }
        }

        Marshal.Copy(_frameBuffer, 0, output, framesRequested);

        if ( _currentBuffer == null && _producerCompleted )
        {
            currentPlaybackCompletion?.TrySetResult(true);

            return StreamCallbackResult.Complete;
        }

        if ( ct.IsCancellationRequested )
        {
            currentPlaybackCompletion?.TrySetResult(false);

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

    private sealed class AudioBuffer(Memory<float> data)
    {
        public Memory<float> Data { get; } = data;

        public int Position { get; private set; } = 0;

        public int Remaining => Data.Length - Position;

        public bool IsFinished => Position >= Data.Length;

        public void Advance(int count) { Position += count; }
    }
}