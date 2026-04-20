namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Represents an audio source that can be awaited for audio data.
/// </summary>
/// <remarks>
///     Important: This should be used at most by one writer and one reader.
///     It can store samples as floats or as bytes, or both. By default, it stores samples as floats.
///     Based on your usage, you can choose to store samples as bytes, floats, or both.
///     If storing them as floats, they will be deserialized from bytes when they are added and returned directly when
///     requested.
///     If storing them as bytes, they will be serialized from floats when they are added and returned directly when
///     requested.
///     If you want to optimize your memory usage, you can store them in the same format as they are added.
///     If you want to optimize your CPU usage, you can store them in the format you want to use them in.
/// </remarks>
public class AwaitableAudioSource(
    IReadOnlyDictionary<string, string> metadata,
    bool storeSamples = true,
    bool storeBytes = false,
    int initialSizeFloats = BufferedMemoryAudioSource.DefaultInitialSize,
    int initialSizeBytes = BufferedMemoryAudioSource.DefaultInitialSize,
    IChannelAggregationStrategy? aggregationStrategy = null
)
    : DiscardableMemoryAudioSource(
        metadata,
        storeSamples,
        storeBytes,
        initialSizeFloats,
        initialSizeBytes,
        aggregationStrategy
    ),
        IAwaitableAudioSource
{
    private readonly TaskCompletionSource<bool> initializationTcs = new();

    private readonly AsyncAutoResetEvent samplesAvailableEvent = new();

    protected readonly Lock syncRoot = new();

    /// <summary>
    ///     Gets a value indicating whether the source is flushed.
    /// </summary>
    public bool IsFlushed { get; private set; }

    public override Task<Memory<byte>> GetFramesAsync(
        long startFrame,
        int maxFrames = int.MaxValue,
        CancellationToken cancellationToken = default
    )
    {
        lock (syncRoot)
        {
            // Calling the base method with lock is fine here, as the base method will not await anything.
            return base.GetFramesAsync(startFrame, maxFrames, cancellationToken);
        }
    }

    public override Task<int> CopyFramesAsync(
        Memory<byte> destination,
        long startFrame,
        int maxFrames = int.MaxValue,
        CancellationToken cancellationToken = default
    )
    {
        lock (syncRoot)
        {
            // Calling the base method with lock is fine here, as the base method will not await anything.
            return base.CopyFramesAsync(destination, startFrame, maxFrames, cancellationToken);
        }
    }

    public override Task<Memory<float>> GetSamplesAsync(
        long startFrame,
        int maxFrames = int.MaxValue,
        CancellationToken cancellationToken = default
    )
    {
        lock (syncRoot)
        {
            // Calling the base method with lock is fine here, as the base method will not await anything.
            return base.GetSamplesAsync(startFrame, maxFrames, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task WaitForNewSamplesAsync(long sampleCount, CancellationToken cancellationToken)
    {
        while (!IsFlushed && SampleVirtualCount <= sampleCount)
        {
            await samplesAvailableEvent
                .WaitAsync()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task WaitForNewSamplesAsync(
        TimeSpan minimumDuration,
        CancellationToken cancellationToken
    )
    {
        while (!IsFlushed && Duration <= minimumDuration)
        {
            await samplesAvailableEvent
                .WaitAsync()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task WaitForInitializationAsync(CancellationToken cancellationToken)
    {
        if (IsInitialized)
        {
            return;
        }

        await initializationTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Flush()
    {
        lock (syncRoot)
        {
            IsFlushed = true;
            samplesAvailableEvent.Set();
        }
    }

    public override void DiscardFrames(int count)
    {
        lock (syncRoot)
        {
            base.DiscardFrames(count);
        }
    }

    public override void AddFrame(ReadOnlyMemory<byte> frame)
    {
        lock (syncRoot)
        {
            if (IsFlushed)
            {
                throw new InvalidOperationException(
                    "The source is flushed and cannot accept new frames."
                );
            }

            base.AddFrame(frame);
            if (IsInitialized && !initializationTcs.Task.IsCompleted)
            {
                initializationTcs.SetResult(true);
            }
        }
    }

    public override void AddFrame(ReadOnlyMemory<float> frame)
    {
        lock (syncRoot)
        {
            if (IsFlushed)
            {
                throw new InvalidOperationException(
                    "The source is flushed and cannot accept new frames."
                );
            }

            base.AddFrame(frame);
            if (IsInitialized && !initializationTcs.Task.IsCompleted)
            {
                initializationTcs.SetResult(true);
            }
        }
    }

    /// <summary>
    ///     Notifies that new samples are available.
    /// </summary>
    public void NotifyNewSamples()
    {
        samplesAvailableEvent.Set();
    }
}
