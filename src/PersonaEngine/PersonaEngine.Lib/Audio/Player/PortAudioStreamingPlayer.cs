using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PersonaEngine.Lib.TTS.Synthesis;

using PortAudioSharp;
// Assuming AudioSegment is here
using Stream = PortAudioSharp.Stream;

namespace PersonaEngine.Lib.Audio.Player;

/// <summary>
///     PortAudio implementation of <see cref="IStreamingAudioPlayer" /> for real-time audio playback.
///     This implementation is thread-safe and designed for low-latency audio processing.
/// </summary>
public sealed class PortAudioStreamingPlayer : IStreamingAudioPlayer, IStreamingAudioPlayerHost
{
    // --- Constants ---
    public const int DefaultSampleRate = 24000;

    public const int DefaultMaxBufferCount = 100;

    public const int DefaultFrameBufferSize = 1024;

    private readonly CancellationTokenSource _eventProcessingCts; // For stopping the event processing loop on dispose

    private readonly float[] _frameBuffer; // Pre-allocated buffer for the audio callback

    private readonly uint _frameBufferSize;

    // --- Dependencies and Configuration ---
    private readonly ILogger<PortAudioStreamingPlayer> _logger;

    private readonly int _maxBufferCount;

    // --- Event Processing ---
    private readonly ConcurrentQueue<(AudioSegment Segment, bool IsStart)> _pendingEvents = new();

    private readonly SemaphoreSlim _playbackLock = new(1, 1); // Ensures only one StartPlaybackAsync runs at a time

    private readonly int _sampleRate;

    // --- Synchronization and State ---
    private readonly object _stateLock = new();

    private BlockingCollection<AudioBuffer> _audioQueue; // *** Will be recreated for each playback ***

    // --- Core Playback Components ---
    private Stream? _audioStream;

    private AudioBuffer? _currentBuffer; // The buffer currently being read in the callback

    // --- Relative Time Tracking ---
    private long _currentSegmentStartTimeBits = BitConverter.DoubleToInt64Bits(-1.0);

    private volatile bool _isDisposed;

    private long _lastCallbackAbsoluteTimeBits = BitConverter.DoubleToInt64Bits(0.0);

    private TaskCompletionSource<bool>? _playbackCompletion; // Signals when playback finishes/stops/errors

    // --- Playback Session Management ---
    private CancellationTokenSource? _playbackCts; // For cancelling the *current* playback operation

    private volatile bool _producerCompleted; // Flag indicating the producer task finished adding segments for the *current* playback

    /// <summary>
    ///     Initializes a new instance of the <see cref="PortAudioStreamingPlayer" /> class.
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz. Default is 24000.</param>
    /// <param name="maxBufferCount">Maximum number of audio buffers to queue. Default is 100.</param>
    /// <param name="frameBufferSize">Size of each frame buffer in samples. Default is 1024.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <exception cref="ArgumentException">Thrown on invalid parameters.</exception>
    /// <exception cref="AudioPlayerInitializationException">Thrown on PortAudio initialization failure.</exception>
    public PortAudioStreamingPlayer(
        int                                sampleRate      = DefaultSampleRate,
        int                                maxBufferCount  = DefaultMaxBufferCount,
        uint                               frameBufferSize = DefaultFrameBufferSize,
        ILogger<PortAudioStreamingPlayer>? logger          = null)
    {
        // Parameter validation... (same as before)
        _sampleRate      = sampleRate > 0 ? sampleRate : throw new ArgumentException("Sample rate must be positive.", nameof(sampleRate));
        _maxBufferCount  = maxBufferCount > 0 ? maxBufferCount : throw new ArgumentException("Max buffer count must be positive.", nameof(maxBufferCount));
        _frameBufferSize = frameBufferSize > 0 ? frameBufferSize : throw new ArgumentException("Frame buffer size must be positive.", nameof(frameBufferSize));
        _logger          = logger ?? NullLogger<PortAudioStreamingPlayer>.Instance;

        _frameBuffer = new float[frameBufferSize];

        // Create the *initial* audio queue instance
        _audioQueue = new BlockingCollection<AudioBuffer>(_maxBufferCount);

        try
        {
            InitializePortAudio();
            _eventProcessingCts = new CancellationTokenSource();
            _                   = Task.Run(ProcessEventsAsync, CancellationToken.None);
        }
        catch
        {
            // Cleanup if constructor fails
            _audioQueue.Dispose(); // Dispose the initial queue
            _playbackLock.Dispose();

            // PortAudio termination handled within InitializePortAudio on failure
            throw;
        }
    }

    /// <summary>
    ///     Asynchronously starts playback of the provided audio segments.
    ///     If called while playback is already in progress, the current playback will be stopped first.
    /// </summary>
    /// <param name="audioSegments">The asynchronous stream of audio segments to play.</param>
    /// <param name="cancellationToken">Token to cancel the entire playback operation (including enqueuing and playing).</param>
    /// <returns>A task that completes when playback finishes, is stopped, or an error occurs.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the player has been disposed.</exception>
    /// <exception cref="OperationCanceledException">
    ///     Thrown if the operation is cancelled via the
    ///     <paramref name="cancellationToken" />.
    /// </exception>
    /// <exception cref="TimeoutException">Thrown if unable to acquire the playback lock quickly.</exception>
    /// <exception cref="AudioException">Thrown if an error occurs during playback setup or execution.</exception>
    public async Task StartPlaybackAsync(
        IAsyncEnumerable<AudioSegment> audioSegments,
        CancellationToken              cancellationToken = default)
    {
        ThrowIfDisposed();

        // 1. Acquire Lock
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await _playbackLock.WaitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogError("Timeout waiting to acquire playback lock.");

            throw new TimeoutException("Timed out waiting for previous playback to complete or release lock.");
        }
        // Allow OperationCanceledException due to external cancellation token to propagate

        // Capture the queue instance that needs cleanup *before* creating a new one
        var                             queueToDispose = _audioQueue;
        BlockingCollection<AudioBuffer> newAudioQueue;
        TaskCompletionSource<bool>      newPlaybackCompletion;
        CancellationTokenSource         newPlaybackCts;

        try
        {
            // 2. Stop Previous Playback (operates on queueToDispose implicitly via _audioQueue reference at time of call)
            await StopPlaybackInternalAsync(true).ConfigureAwait(false);

            // 3. *** Reset/Recreate Queue for New Playback ***
            try
            {
                queueToDispose?.Dispose(); // Dispose the old queue instance
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception disposing old audio queue. Ignoring.");
            }

            newAudioQueue = new BlockingCollection<AudioBuffer>(_maxBufferCount);
            _audioQueue   = newAudioQueue; // Atomically update the reference used by the callback/producer
            _logger.LogDebug("Created new audio queue for playback.");

            // 4. Prepare New Playback State (under lock)
            lock (_stateLock)
            {
                if ( _isDisposed )
                {
                    throw new ObjectDisposedException(nameof(PortAudioStreamingPlayer));
                }

                State              = PlayerState.Starting;
                _producerCompleted = false;
                _playbackCts?.Dispose(); // Dispose previous CTS
                _playbackCts        = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _playbackCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                // Capture locals for tasks
                newPlaybackCompletion = _playbackCompletion;
                newPlaybackCts        = _playbackCts;
            }

            // Reset time tracking and clear events
            Interlocked.Exchange(ref _currentSegmentStartTimeBits, BitConverter.DoubleToInt64Bits(-1.0));
            Interlocked.Exchange(ref _lastCallbackAbsoluteTimeBits, BitConverter.DoubleToInt64Bits(0.0));
            while ( _pendingEvents.TryDequeue(out _) ) { }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during playback preparation.");
            _playbackLock.Release();
            SetErrorState(ex);
            queueToDispose?.Dispose(); // Ensure cleanup even on error

            throw;
        }

        // 5. Start Producer and Audio Stream
        Task? producerTask  = null;
        var   streamStarted = false;
        try
        {
            // Pass the *new* queue and CTS token to the producer
            producerTask = EnqueueAudioSegmentsAsync(audioSegments, newAudioQueue, newPlaybackCts.Token);

            StartAudioStream();
            streamStarted = true;

            lock (_stateLock)
            {
                if ( State == PlayerState.Starting )
                {
                    State = PlayerState.Playing;
                }
            }

            _logger.LogInformation("Playback starting.");

            // 6. Wait for Completion (logic remains the same)
            await Task.WhenAny(producerTask, newPlaybackCompletion.Task).ConfigureAwait(false);

            if ( producerTask.IsCompleted )
            {
                await producerTask.ConfigureAwait(false);
                _logger.LogDebug("Producer task completed.");
                // Note: _producerCompleted flag is set by EnqueueAudioSegmentsAsync itself now
                await newPlaybackCompletion.Task.ConfigureAwait(false);
            }
            else
            {
                _logger.LogDebug("Playback completion task finished before producer.");
                if ( !newPlaybackCts.IsCancellationRequested )
                {
                    try
                    {
                        await newPlaybackCts.CancelAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        /* Ignore */
                    }
                }

                await newPlaybackCompletion.Task.ConfigureAwait(false);
            }

            _logger.LogInformation("Playback finished gracefully or was stopped.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || (newPlaybackCts?.IsCancellationRequested ?? false))
        {
            _logger.LogInformation("Playback cancelled.");

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during playback execution.");
            SetErrorState(ex);
            await StopPlaybackInternalAsync().ConfigureAwait(false);
            newPlaybackCompletion?.TrySetException(ex);

            throw new AudioException("Error during audio playback execution.", ex);
        }
        finally
        {
            // Final cleanup for this playback session
            if ( streamStarted )
            {
                try
                {
                    StopAudioStream();
                }
                catch (Exception stopEx)
                {
                    _logger.LogWarning(stopEx, "Error stopping audio stream in finally block.");
                }
            }

            lock (_stateLock)
            {
                if ( State == PlayerState.Playing || State == PlayerState.Starting )
                {
                    State = PlayerState.Stopped;
                }
            }

            // Ensure the producer task is awaited if it faulted/cancelled to observe exceptions
            if ( producerTask != null && !(producerTask.IsCompletedSuccessfully || newPlaybackCompletion.Task.IsCompleted) )
            {
                try
                {
                    await producerTask.ConfigureAwait(false);
                }
                catch (Exception prodEx)
                {
                    _logger.LogWarning(prodEx, "Producer task exception observed in finally block.");
                }
            }

            _playbackLock.Release();
        }
    }

    /// <summary>
    ///     Asynchronously disposes resources used by the player.
    ///     Stops any active playback before releasing resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if ( _isDisposed )
        {
            return;
        }

        _isDisposed = true;

        _logger.LogInformation("Disposing PortAudioStreamingPlayer...");

        // 1. Cancel Event Processing
        try
        {
            _eventProcessingCts?.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cancelling event processing CTS.");
        }

        // 2. Stop Playback
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await StopPlaybackInternalAsync(isDisposing: true, cancellationToken: cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Timed out stopping playback during disposal.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping playback during disposal.");
        }

        // 3. Dispose Managed Resources
        _audioStream?.Dispose();
        _playbackCts?.Dispose();
        _playbackLock.Dispose();

        // Dispose the *last* audio queue instance
        _audioQueue?.CompleteAdding(); // Ensure complete before dispose
        _audioQueue?.Dispose();

        _eventProcessingCts?.Dispose();

        // 4. Terminate PortAudio (logic remains the same)
        var needsTermination = false;
        lock (_stateLock)
        {
            if ( State != PlayerState.Uninitialized )
            {
                needsTermination = true;
                State            = PlayerState.Uninitialized;
            }
        }

        if ( needsTermination )
        {
            try
            {
                PortAudio.Terminate();
                _logger.LogInformation("PortAudio terminated.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error terminating PortAudio.");
            }
        }

        OnPlaybackStarted   = null;
        OnPlaybackCompleted = null;

        _logger.LogInformation("PortAudioStreamingPlayer disposed.");
    }

    /// <summary>
    ///     Stops the current playback asynchronously.
    ///     If no playback is active, this method returns immediately.
    /// </summary>
    public Task StopPlaybackAsync()
    {
        ThrowIfDisposed();
        _logger.LogInformation("External stop requested.");

        return StopPlaybackInternalAsync();
    }

    public PlayerState State { get; private set; } = PlayerState.Uninitialized;

    /// <summary>
    ///     Gets the current playback time in seconds, relative to the start of the currently playing audio segment.
    ///     Returns 0 if no segment is currently playing.
    /// </summary>
    public float CurrentTime
    {
        get
        {
            if ( _isDisposed || State == PlayerState.Uninitialized )
            {
                return 0f;
            }

            var startBits   = Interlocked.Read(ref _currentSegmentStartTimeBits);
            var currentBits = Interlocked.Read(ref _lastCallbackAbsoluteTimeBits);
            if ( startBits == BitConverter.DoubleToInt64Bits(-1.0) || currentBits < startBits )
            {
                return 0f;
            }

            var startTime   = BitConverter.Int64BitsToDouble(startBits);
            var currentTime = BitConverter.Int64BitsToDouble(currentBits);

            return (float)(currentTime - startTime);
        }
    }

    /// <summary>
    ///     Occurs when playback of an audio segment starts.
    /// </summary>
    public event EventHandler<AudioPlaybackEventArgs>? OnPlaybackStarted;

    /// <summary>
    ///     Occurs when playback of an audio segment completes.
    /// </summary>
    public event EventHandler<AudioPlaybackEventArgs>? OnPlaybackCompleted;

    // =====================================
    // Internal Implementation Details
    // =====================================

    /// <summary>
    ///     Centralized method to stop playback, clean up resources for the current session,
    ///     and signal completion. Designed to be safe to call multiple times or when not playing.
    /// </summary>
    /// <param name="isStartingNew">If true, indicates this stop is part of starting a new playback session.</param>
    /// <param name="isDisposing">If true, indicates this stop is part of the disposal process.</param>
    /// <param name="cancellationToken">Optional token for cancellation during cleanup waits.</param>
    private async Task StopPlaybackInternalAsync(bool isStartingNew = false, bool isDisposing = false, CancellationToken cancellationToken = default)
    {
        PlayerState                      currentState;
        CancellationTokenSource?         currentPlaybackCts;
        TaskCompletionSource<bool>?      currentPlaybackCompletion;
        BlockingCollection<AudioBuffer>? queueToStop;

        lock (_stateLock)
        {
            queueToStop  = _audioQueue;
            currentState = State;
            // ... (rest of the state checks remain the same) ...
            if ( State == PlayerState.Stopping && !isDisposing )
            {
                _logger.LogDebug("Stop already in progress.");

                // If stop is already in progress, we might still need to wait for its TCS
                // but avoid re-executing the stop logic. However, for simplicity now,
                // let's just return if already stopping unless disposing.
                return;
            }

            State                     = PlayerState.Stopping;
            currentPlaybackCts        = _playbackCts;
            currentPlaybackCompletion = _playbackCompletion; // Capture the TCS
        }

        _logger.LogDebug("Stopping playback (State: {CurrentState}, IsStartingNew: {IsStartingNew}, IsDisposing: {IsDisposing})", currentState, isStartingNew, isDisposing);

        // 1. Signal Cancellation (if not already)
        if ( currentPlaybackCts != null && !currentPlaybackCts.IsCancellationRequested )
        {
            try
            {
                await currentPlaybackCts.CancelAsync().ConfigureAwait(false);
                _logger.LogDebug("Playback CTS cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cancelling playback CTS.");
            }
        }

        // 2. Signal Producer for *this specific queue* to stop adding
        queueToStop?.CompleteAdding();

        // *** 3. Signal Playback Completion Task *BEFORE* stopping stream ***
        // Signal that the stop has been initiated and cleanup is proceeding.
        var signaled = currentPlaybackCompletion?.TrySetResult(false) ?? false; // false = stopped, not completed naturally
        if ( signaled )
        {
            _logger.LogDebug("Playback completion TCS signaled as stopped (before stream stop).");
        }
        else
        {
            _logger.LogDebug("Playback completion TCS was already completed or null.");
        } // Log if it was already set

        // 4. Stop Audio Stream (Potentially Blocking)
        try
        {
            _logger.LogDebug("Attempting to stop audio stream...");
            StopAudioStream(); // This might still hang, but the TCS is already set
            _logger.LogDebug("Audio stream stop call returned.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping audio stream.");
        }

        // 5. Clean up Queue and Current Buffer for the stopped session
        _currentBuffer = null;
        if ( queueToStop != null )
        {
            _logger.LogDebug("Draining queue associated with the stopped playback.");
            while ( queueToStop.TryTake(out _) ) { }
        }

        _producerCompleted = false; // Reset flag

        // 6. Final State Update (No longer need to signal TCS here)
        lock (_stateLock)
        {
            if ( State == PlayerState.Stopping )
            {
                State = isDisposing ? PlayerState.Uninitialized : PlayerState.Stopped;
                _logger.LogDebug("State set to {NewState}", State);
            }

            if ( !isStartingNew && !isDisposing )
            {
                _playbackCts?.Dispose();
                _playbackCts = null;
            }
        }

        // 7. Reset time tracking only if not disposing
        if ( !isDisposing )
        {
            Interlocked.Exchange(ref _currentSegmentStartTimeBits, BitConverter.DoubleToInt64Bits(-1.0));
            Interlocked.Exchange(ref _lastCallbackAbsoluteTimeBits, BitConverter.DoubleToInt64Bits(0.0));
        }

        _logger.LogDebug("StopPlaybackInternalAsync finished."); // Add log
    }

    private async Task EnqueueAudioSegmentsAsync(
        IAsyncEnumerable<AudioSegment>  audioSegments,
        BlockingCollection<AudioBuffer> targetQueue, // Accept the target queue instance
        CancellationToken               cancellationToken)
    {
        _logger.LogDebug("Starting audio segment producer task for the current playback.");
        var markProducerComplete = false;
        try
        {
            await foreach ( var segment in audioSegments.WithCancellation(cancellationToken) )
            {
                if ( segment.AudioData.Length <= 0 )
                {
                    continue;
                }

                var buffer = new AudioBuffer(segment.AudioData, segment);
                try
                {
                    // Add to the specific queue instance provided
                    targetQueue.Add(buffer, cancellationToken);
                }
                catch (InvalidOperationException) when (targetQueue.IsAddingCompleted)
                {
                    _logger.LogInformation("Target audio queue completed while adding segment. Stopping producer.");

                    break;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Producer task cancelled while adding segment.");

                    break;
                }
            }

            // If we finished the loop *without* cancellation, mark for completion
            if ( !cancellationToken.IsCancellationRequested )
            {
                markProducerComplete = true;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Producer task cancelled during enumeration.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in audio segment producer task.");
            _playbackCompletion?.TrySetException(new AudioException("Producer task failed.", ex));
            SetErrorState(ex);
            _playbackCts?.Cancel(); // Ensure cancellation on error
        }
        finally
        {
            // Set the volatile flag if completed naturally
            if ( markProducerComplete )
            {
                _producerCompleted = true;
                _logger.LogDebug("Producer task marked as completed naturally.");
            }

            // Signal the *specific* target queue that this producer is done adding.
            // This is crucial for the callback to detect the end-of-stream correctly for *this* playback.
            targetQueue.CompleteAdding();
            _logger.LogDebug("Producer task finished. Target queue adding completed.");
        }
    }

    private StreamCallbackResult AudioCallback(
        IntPtr                     input,    IntPtr              output,      uint   frameCount,
        ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userData)
    {
        // --- Pre-computation and Checks ---
        var framesRequested = (int)frameCount;
        var currentQueue    = _audioQueue; // Use the instance currently assigned
        // Ensure we capture the specific completion source and CTS for *this* playback session
        var currentPlaybackCts        = _playbackCts;
        var currentPlaybackCompletion = _playbackCompletion;
        var ct                        = currentPlaybackCts?.Token ?? CancellationToken.None;

        Interlocked.Exchange(ref _lastCallbackAbsoluteTimeBits, BitConverter.DoubleToInt64Bits(timeInfo.currentTime));
        if ( statusFlags.HasFlag(StreamCallbackFlags.OutputUnderflow) )
        {
            _logger.LogWarning("PortAudio output underflow detected.");
        }

        if ( statusFlags.HasFlag(StreamCallbackFlags.OutputOverflow) )
        {
            _logger.LogWarning("PortAudio output overflow detected.");
        }

        // --- Cancellation Check ---
        if ( ct.IsCancellationRequested )
        {
            _logger.LogDebug("Audio callback cancelled.");
            if ( _currentBuffer != null )
            {
                _pendingEvents.Enqueue((_currentBuffer.Segment, false));
            }

            _currentBuffer = null;
            Interlocked.Exchange(ref _currentSegmentStartTimeBits, BitConverter.DoubleToInt64Bits(-1.0));

            // *** ADDED: Ensure completion task is signaled on cancellation ***
            var signaled = currentPlaybackCompletion?.TrySetResult(false) ?? false;
            if ( signaled )
            {
                _logger.LogDebug("Audio callback signaled completion TCS as 'stopped' due to cancellation.");
            }

            return StreamCallbackResult.Complete; // Stop processing audio frames
        }

        // --- Fill Output Buffer ---
        var framesWritten = 0;
        try
        {
            while ( framesWritten < framesRequested )
            {
                // 1. Get next buffer if needed
                if ( _currentBuffer == null )
                {
                    // TryTake from the *current* queue instance
                    if ( !currentQueue.TryTake(out _currentBuffer) )
                    {
                        // Check IsCompleted *on the current queue instance* and the volatile flag
                        // Use the producer flag associated with this playback (read directly)
                        if ( _producerCompleted && currentQueue.IsCompleted )
                        {
                            _logger.LogDebug("Audio callback: All buffers played and producer completed for the current queue.");
                            // Use captured TCS instance
                            var completedNaturally = currentPlaybackCompletion?.TrySetResult(true) ?? false; // Signal natural completion
                            if ( completedNaturally )
                            {
                                _logger.LogDebug("Audio callback signaled completion TCS as 'completed naturally'.");
                            }

                            Interlocked.Exchange(ref _currentSegmentStartTimeBits, BitConverter.DoubleToInt64Bits(-1.0));

                            goto FillSilence;
                        }

                        // Queue is empty, but producer isn't done or hasn't marked queue complete yet
                        // OR cancellation happened between TryTake and the _producerCompleted check
                        if ( ct.IsCancellationRequested ) // Re-check cancellation if queue is empty but not completed
                        {
                            _logger.LogDebug("Audio callback cancelled while waiting for buffer.");
                            var signaledOnEmpty = currentPlaybackCompletion?.TrySetResult(false) ?? false;
                            if ( signaledOnEmpty )
                            {
                                _logger.LogDebug("Audio callback signaled completion TCS as 'stopped' (empty queue/cancelled).");
                            }

                            return StreamCallbackResult.Complete;
                        }

                        goto FillSilence; // Fill with silence and continue polling
                    }

                    // New buffer obtained
                    _logger.LogTrace("Audio callback: Starting segment {SegmentId}", _currentBuffer.Segment.Id);
                    _pendingEvents.Enqueue((_currentBuffer.Segment, true));
                    Interlocked.Exchange(ref _currentSegmentStartTimeBits, BitConverter.DoubleToInt64Bits(timeInfo.currentTime));
                }

                // 2. Copy data (logic remains the same)
                var framesToCopy = Math.Min(framesRequested - framesWritten, _currentBuffer.Remaining);
                var sourceSpan   = _currentBuffer.Data.Span.Slice(_currentBuffer.Position, framesToCopy);
                // Check if _frameBuffer is large enough (should be, but safety check)
                if ( framesWritten + framesToCopy > _frameBuffer.Length )
                {
                    _logger.LogError("Audio callback buffer logic error: Attempting to write beyond _frameBuffer bounds.");
                    // Handle error: signal completion with exception?
                    currentPlaybackCompletion?.TrySetException(new IndexOutOfRangeException("Audio frame buffer write out of bounds."));

                    return StreamCallbackResult.Abort;
                }

                var destinationSpan = _frameBuffer.AsSpan(framesWritten, framesToCopy);
                sourceSpan.CopyTo(destinationSpan);
                _currentBuffer.Advance(framesToCopy);
                framesWritten += framesToCopy;

                // 3. Check if buffer finished (logic remains the same)
                if ( _currentBuffer.IsFinished )
                {
                    _logger.LogTrace("Audio callback: Finished segment {SegmentId}", _currentBuffer.Segment.Id);
                    _pendingEvents.Enqueue((_currentBuffer.Segment, false));
                    _currentBuffer = null;
                    Interlocked.Exchange(ref _currentSegmentStartTimeBits, BitConverter.DoubleToInt64Bits(-1.0));
                }
            }

            FillSilence:
            if ( framesWritten < framesRequested )
            {
                // Check if _frameBuffer is large enough (should be, but safety check)
                if ( framesWritten > _frameBuffer.Length || framesRequested - framesWritten < 0 || framesWritten + (framesRequested - framesWritten) > _frameBuffer.Length )
                {
                    _logger.LogError("Audio callback buffer logic error: Attempting to clear beyond _frameBuffer bounds.");
                    currentPlaybackCompletion?.TrySetException(new IndexOutOfRangeException("Audio frame buffer clear out of bounds."));

                    return StreamCallbackResult.Abort;
                }

                Array.Clear(_frameBuffer, framesWritten, framesRequested - framesWritten);
            }

            Marshal.Copy(_frameBuffer, 0, output, framesRequested);

            // Final check for completion AFTER writing data
            if ( _currentBuffer == null && _producerCompleted && currentQueue.IsCompleted )
            {
                _logger.LogDebug("Audio callback: Final check indicates natural completion.");
                var completedNaturallyFinal = currentPlaybackCompletion?.TrySetResult(true) ?? false;
                if ( completedNaturallyFinal )
                {
                    _logger.LogDebug("Audio callback signaled completion TCS as 'completed naturally' (final check).");
                }

                return StreamCallbackResult.Complete;
            }

            // Re-check cancellation one last time before returning Continue
            if ( ct.IsCancellationRequested )
            {
                _logger.LogDebug("Audio callback cancelled (final check).");
                var signaledFinal = currentPlaybackCompletion?.TrySetResult(false) ?? false;
                if ( signaledFinal )
                {
                    _logger.LogDebug("Audio callback signaled completion TCS as 'stopped' (final check).");
                }

                return StreamCallbackResult.Complete;
            }

            return StreamCallbackResult.Continue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in audio callback.");
            SetErrorState(ex);
            // Use captured TCS instance
            currentPlaybackCompletion?.TrySetException(new AudioException("Error during audio callback.", ex));
            Interlocked.Exchange(ref _currentSegmentStartTimeBits, BitConverter.DoubleToInt64Bits(-1.0));

            return StreamCallbackResult.Abort;
        }
    }

    // Separate task to process events outside the real-time audio thread
    private async Task ProcessEventsAsync()
    {
        _logger.LogDebug("Event processing task started.");
        var token = _eventProcessingCts.Token;
        while ( !token.IsCancellationRequested )
        {
            try
            {
                var eventProcessed = false;
                while ( _pendingEvents.TryDequeue(out var eventInfo) )
                {
                    eventProcessed = true;
                    if ( token.IsCancellationRequested )
                    {
                        break; // Check token frequently
                    }

                    try
                    {
                        if ( eventInfo.IsStart )
                        {
                            _logger.LogTrace("Raising OnPlaybackStarted for segment {SegmentId}", eventInfo.Segment.Id);
                            OnPlaybackStarted?.Invoke(this, new AudioPlaybackEventArgs(eventInfo.Segment));
                        }
                        else
                        {
                            _logger.LogTrace("Raising OnPlaybackCompleted for segment {SegmentId}", eventInfo.Segment.Id);
                            OnPlaybackCompleted?.Invoke(this, new AudioPlaybackEventArgs(eventInfo.Segment));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error executing playback event handler ({EventType})", eventInfo.IsStart ? "Started" : "Completed");
                        // Continue processing other events
                    }
                }

                if ( !eventProcessed )
                {
                    // Wait efficiently if queue is empty
                    await Task.Delay(TimeSpan.FromMilliseconds(10), token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _logger.LogInformation("Event processing task cancelled.");

                break; // Exit loop
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in event processing loop. Continuing after delay.");
                // Avoid tight loop on persistent error
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogDebug("Event processing task stopped.");
    }

    private void InitializePortAudio()
    {
        lock (_stateLock) // Lock ensures state transitions are safe if called concurrently (shouldn't be)
        {
            if ( State != PlayerState.Uninitialized )
            {
                _logger.LogWarning("PortAudio already initialized or in an unexpected state ({State}). Skipping initialization.", State);

                return;
            }

            _logger.LogInformation("Initializing PortAudio...");
            try
            {
                PortAudio.Initialize();
                var deviceIndex = PortAudio.DefaultOutputDevice;
                if ( deviceIndex == PortAudio.NoDevice )
                {
                    throw new AudioDeviceNotFoundException("No default PortAudio output device found.");
                }

                var deviceInfo = PortAudio.GetDeviceInfo(deviceIndex);
                _logger.LogInformation("Using PortAudio output device: {DeviceName} (Index: {DeviceIndex})", deviceInfo.name, deviceIndex);

                var parameters = new StreamParameters {
                                                          device                    = deviceIndex,
                                                          channelCount              = 1,                                  // Mono
                                                          sampleFormat              = SampleFormat.Float32,               // We use float internally
                                                          suggestedLatency          = deviceInfo.defaultLowOutputLatency, // Target low latency
                                                          hostApiSpecificStreamInfo = IntPtr.Zero
                                                      };

                // Create the stream - **DO NOT START IT HERE**
                _audioStream = new Stream(
                                          null,       // No input
                                          parameters, // Output parameters
                                          _sampleRate,
                                          _frameBufferSize,
                                          StreamFlags.ClipOff, // Assume input is already clipped [-1.0, 1.0] - important! Add ClipOn if unsure.
                                          AudioCallback,       // Our callback function
                                          IntPtr.Zero);        // No user data passed directly to callback

                State = PlayerState.Initialized;
                _logger.LogInformation("PortAudio initialized successfully, stream created.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize PortAudio or create stream.");
                State = PlayerState.Error; // Mark state as error
                try
                {
                    // Attempt cleanup if initialization failed mid-way
                    PortAudio.Terminate();
                }
                catch (Exception termEx)
                {
                    _logger.LogError(termEx, "Error during PortAudio termination after initialization failure.");
                }

                throw new AudioPlayerInitializationException("Failed to initialize PortAudio audio system.", ex);
            }
        }
    }

    private void StartAudioStream()
    {
        lock (_stateLock)
        {
            if ( _isDisposed )
            {
                throw new ObjectDisposedException(nameof(PortAudioStreamingPlayer));
            }

            if ( State != PlayerState.Initialized && State != PlayerState.Starting && State != PlayerState.Stopped )
            {
                _logger.LogWarning("Cannot start audio stream in state {State}", State);
                // Allow starting from Stopped state if restarting playback
                if ( State != PlayerState.Stopped )
                {
                    throw new InvalidOperationException($"Cannot start audio stream in the current state: {State}");
                }
            }

            if ( _audioStream == null )
            {
                throw new InvalidOperationException("Audio stream is not initialized.");
            }

            if ( _audioStream.IsActive )
            {
                _logger.LogWarning("Audio stream is already active.");

                return; // Or throw? Current logic assumes Stop was called first.
            }

            try
            {
                _logger.LogDebug("Starting PortAudio stream...");
                _audioStream.Start();
                _logger.LogInformation("PortAudio stream started.");
                // State transition to Playing happens in StartPlaybackAsync after stream starts
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start PortAudio stream.");
                SetErrorState(ex); // Update state under lock

                throw new AudioException("Failed to start audio playback stream.", ex);
            }
        }
    }

    private void StopAudioStream()
    {
        // No lock here - stream operations should be thread-safe per PortAudio docs,
        // and we want to avoid potential deadlocks if called from callback context (though we avoid that).
        // State checks should happen *before* calling this.
        if ( _audioStream == null || !_audioStream.IsActive )
        {
            //_logger.LogTrace("Audio stream is null or not active, skipping stop.");
            return;
        }

        try
        {
            _logger.LogDebug("Stopping PortAudio stream...");
            _audioStream.Stop(); // Should be safe to call even if already stopped
            _logger.LogInformation("PortAudio stream stopped.");
        }
        catch (Exception ex)
        {
            // Log error but don't necessarily transition overall player state here,
            // as the stop might be part of normal cleanup. The caller handles state.
            _logger.LogError(ex, "Error stopping PortAudio stream.");
            // Consider if specific PortAudio exceptions need state change?
        }
    }

    private void SetErrorState(Exception? ex = null)
    {
        lock (_stateLock)
        {
            _logger.LogError(ex, "Player transitioning to Error state.");
            State = PlayerState.Error;
        }

        // Optionally, signal completion with error if a playback was active
        _playbackCompletion?.TrySetException(ex ?? new AudioException("Player entered an error state."));
    }

    private void ThrowIfDisposed()
    {
        if ( _isDisposed )
        {
            throw new ObjectDisposedException(nameof(PortAudioStreamingPlayer));
        }
    }

    /// <summary>
    ///     Internal class representing a buffer to be queued for playback.
    /// </summary>
    private sealed class AudioBuffer
    {
        public AudioBuffer(Memory<float> data, AudioSegment segment)
        {
            Data     = data;
            Segment  = segment;
            Position = 0;
        }

        public Memory<float> Data { get; }

        public AudioSegment Segment { get; }

        public int Position { get; private set; }

        public int Remaining => Data.Length - Position;

        public bool IsFinished => Position >= Data.Length;

        public void Advance(int count) { Position += count; }
    }
}