using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.Audio.Player;

/// <summary>
///     Default implementation of IAudioBufferManager.
/// </summary>
public class DefaultAudioBufferManager : IAudioBufferManager
{
    private readonly BlockingCollection<(Memory<float> Data, AudioSegment Segment)> _audioQueue;

    private readonly ILogger _logger;

    private readonly int _maxBufferCount;

    private bool _disposed;

    public DefaultAudioBufferManager(
        int maxBufferCount = 32,
        ILogger<DefaultAudioBufferManager>? logger = null
    )
    {
        _maxBufferCount =
            maxBufferCount > 0
                ? maxBufferCount
                : throw new ArgumentException(
                    "Max buffer count must be positive",
                    nameof(maxBufferCount)
                );

        _logger = logger ?? NullLogger<DefaultAudioBufferManager>.Instance;
        _audioQueue = new BlockingCollection<(Memory<float>, AudioSegment)>(_maxBufferCount);
    }

    public int BufferCount => _audioQueue.Count;

    public bool ProducerCompleted { get; private set; }

    public async Task EnqueueSegmentsAsync(
        IAsyncEnumerable<AudioSegment> audioSegments,
        CancellationToken cancellationToken
    )
    {
        try
        {
            ProducerCompleted = false;

            await foreach (var segment in audioSegments.WithCancellation(cancellationToken))
            {
                if (segment.AudioData.Length <= 0 || cancellationToken.IsCancellationRequested)
                {
                    continue;
                }

                try
                {
                    // Add to bounded collection - will block if queue is full (applying backpressure)
                    _audioQueue.Add((segment.AudioData, segment), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Expected when cancellation is requested
                    break;
                }
                catch (InvalidOperationException) when (_audioQueue.IsAddingCompleted)
                {
                    // Queue was completed during processing
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Audio enqueuing cancelled");

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while enqueuing audio segments");

            throw;
        }
        finally
        {
            ProducerCompleted = true;
        }
    }

    public bool TryGetNextBuffer(
        out (Memory<float> Data, AudioSegment Segment) buffer,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        return _audioQueue.TryTake(out buffer, timeoutMs, cancellationToken);
    }

    public void Clear()
    {
        while (_audioQueue.TryTake(out _)) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _audioQueue.Dispose();
            await Task.CompletedTask; // For async consistency
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing audio buffer manager");
        }
    }
}
