using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PortAudioSharp;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Audition;

/// <summary>
///     Lightweight PortAudio playback for one-shot audition clips.
///     One active playback at a time — starting a new one cancels the previous.
///     Independent of <c>PortaudioOutputAdapter</c> so it never interacts with the
///     live conversation audio pipeline.
/// </summary>
public sealed class OneShotAudioPlayer : IDisposable, IOneShotPlayer
{
    private const int FrameBufferSize = 512;

    private readonly ILogger<OneShotAudioPlayer> _logger;
    private readonly object _playbackLock = new();

    private CancellationTokenSource? _activeCts;
    private Task? _activeTask;
    private bool _disposed;

    public OneShotAudioPlayer(ILogger<OneShotAudioPlayer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PlayAsync(ReadOnlyMemory<float> pcm, int sampleRate, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        CancellationTokenSource? previousCts;
        Task? previousTask;
        lock (_playbackLock)
        {
            previousCts = _activeCts;
            previousTask = _activeTask;
        }

        previousCts?.Cancel();
        if (previousTask is not null)
        {
            try
            {
                await previousTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            { /* expected */
            }
        }

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var task = Task.Run(() => PlayInternal(pcm, sampleRate, linkedCts.Token), linkedCts.Token);

        lock (_playbackLock)
        {
            _activeCts = linkedCts;
            _activeTask = task;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        { /* expected */
        }
        finally
        {
            linkedCts.Dispose();
            lock (_playbackLock)
            {
                if (ReferenceEquals(_activeCts, linkedCts))
                {
                    _activeCts = null;
                    _activeTask = null;
                }
            }
        }
    }

    private void PlayInternal(ReadOnlyMemory<float> pcm, int sampleRate, CancellationToken ct)
    {
        PortAudioSharp.Stream? stream = null;
        var done = new ManualResetEventSlim(false);

        // Shared callback state — position and cancellation flag.
        // These are read/written from the PortAudio callback thread AND the current thread,
        // so we use volatile int for position and a captured CancellationToken for the flag.
        var position = 0;
        var totalFrames = pcm.Length;
        var frameBuffer = new float[FrameBufferSize];

        StreamCallbackResult Callback(
            IntPtr input,
            IntPtr output,
            uint frameCount,
            ref StreamCallbackTimeInfo timeInfo,
            StreamCallbackFlags statusFlags,
            IntPtr userDataPtr
        )
        {
            if (ct.IsCancellationRequested || position >= totalFrames)
            {
                // Fill output with silence and signal completion.
                var silence = new float[frameCount];
                Marshal.Copy(silence, 0, output, (int)frameCount);
                done.Set();

                return StreamCallbackResult.Complete;
            }

            var requested = (int)frameCount;
            var available = Math.Min(requested, totalFrames - position);
            var span = pcm.Span.Slice(position, available);

            // Copy available audio frames into the frame buffer.
            span.CopyTo(frameBuffer.AsSpan(0, available));

            // Zero-fill any remainder (last chunk smaller than frameCount).
            if (available < requested)
            {
                Array.Clear(frameBuffer, available, requested - available);
            }

            Marshal.Copy(frameBuffer, 0, output, requested);
            position += available;

            if (position >= totalFrames)
            {
                done.Set();

                return StreamCallbackResult.Complete;
            }

            return StreamCallbackResult.Continue;
        }

        try
        {
            PortAudio.Initialize();

            var deviceIndex = PortAudio.DefaultOutputDevice;
            if (deviceIndex == PortAudio.NoDevice)
            {
                _logger.LogWarning(
                    "OneShotAudioPlayer: no default output device found, skipping playback"
                );

                return;
            }

            var deviceInfo = PortAudio.GetDeviceInfo(deviceIndex);

            var outputParams = new StreamParameters
            {
                device = deviceIndex,
                channelCount = 1,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = deviceInfo.defaultLowOutputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero,
            };

            stream = new PortAudioSharp.Stream(
                null,
                outputParams,
                sampleRate,
                FrameBufferSize,
                StreamFlags.ClipOff,
                Callback,
                IntPtr.Zero
            );

            stream.Start();

            // Block until the callback signals completion or cancellation fires.
            done.Wait(ct);

            stream.Stop();
        }
        catch (OperationCanceledException)
        {
            // Propagate so the task shows as cancelled.
            throw;
        }
        catch (Exception ex)
        {
            // Platform may be headless / no device — skip playback, log, don't throw.
            _logger.LogWarning(ex, "OneShotAudioPlayer: could not initialise or play audio");
        }
        finally
        {
            done.Dispose();
            try
            {
                stream?.Stop();
            }
            catch
            { /* already stopped or never started */
            }

            stream?.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        CancellationTokenSource? cts;
        lock (_playbackLock)
        {
            cts = _activeCts;
            _activeCts = null;
            _activeTask = null;
        }

        try
        {
            cts?.Cancel();
        }
        catch
        { /* ignore */
        }

        cts?.Dispose();
    }
}
