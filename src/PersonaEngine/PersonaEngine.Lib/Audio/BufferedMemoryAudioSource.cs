namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Represents an audio source that stores audio samples in memory.
/// </summary>
/// <remarks>
///     It can store samples as floats or as bytes, or both. By default, it stores samples as floats.
///     Based on your usage, you can choose to store samples as bytes, floats, or both.
///     If storing them as floats, they will be deserialized from bytes when they are added and returned directly when
///     requested.
///     If storing them as bytes, they will be serialized from floats when they are added and returned directly when
///     requested.
///     If you want to optimize your memory usage, you can store them in the same format as they are added.
///     If you want to optimize your CPU usage, you can store them in the format you want to use them in.
/// </remarks>
public class BufferedMemoryAudioSource : IAudioSource, IMemoryBackedAudioSource
{
    public const int DefaultInitialSize = 1024 * 16;

    private readonly IChannelAggregationStrategy? aggregationStrategy;

    protected byte[]? ByteFrames;

    protected float[]? FloatFrames;

    private long framesCount;

    private bool isDisposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="BufferedMemoryAudioSource" /> class.
    /// </summary>
    /// <param name="storeSamples"> A value indicating whether to store samples as floats. Default true.</param>
    /// <param name="storeBytes"> A value indicating whether to store samples as byte[]. Default false.</param>
    /// <param name="aggregationStrategy"> Optional. The channel aggregation strategy to use.</param>
    public BufferedMemoryAudioSource(
        IReadOnlyDictionary<string, string> metadata,
        bool storeFloats = true,
        bool storeBytes = false,
        int initialSizeFloats = DefaultInitialSize,
        int initialSizeBytes = DefaultInitialSize,
        IChannelAggregationStrategy? aggregationStrategy = null
    )
    {
        Metadata = metadata;
        this.aggregationStrategy = aggregationStrategy;

        if (!storeFloats && !storeBytes)
        {
            throw new ArgumentException("At least one of storeFloats or storeBytes must be true.");
        }

        if (storeFloats)
        {
            FloatFrames = new float[initialSizeFloats];
        }

        if (storeBytes)
        {
            ByteFrames = new byte[initialSizeBytes];
        }
    }

    /// <summary>
    ///     Gets the header of the audio source.
    /// </summary>
    protected AudioSourceHeader Header { get; private set; } = null!;

    /// <summary>
    ///     Gets the size of a single frame in the current wave file.
    /// </summary>
    public int FrameSize => BitsPerSample * ChannelCount / 8;

    /// <summary>
    ///     Represents the actual number of channels in the source.
    /// </summary>
    /// <remarks>
    ///     Note, that the actual number of channels may be different from the number of channels in the header if the source
    ///     uses an aggregation strategy.
    /// </remarks>
    public ushort ChannelCount => aggregationStrategy == null ? Header.Channels : (ushort)1;

    /// <summary>
    ///     Gets the number of samples for each channel.
    /// </summary>
    public virtual long FramesCount => framesCount;

    /// <summary>
    ///     Gets a value indicating whether the source is initialized.
    /// </summary>
    public bool IsInitialized { get; private set; }

    public uint SampleRate => Header.SampleRate;

    public ushort BitsPerSample => Header.BitsPerSample;

    public IReadOnlyDictionary<string, string> Metadata { get; }

    public virtual TimeSpan Duration => TimeSpan.FromMilliseconds(FramesCount * 1000d / SampleRate);

    public virtual TimeSpan TotalDuration =>
        TimeSpan.FromMilliseconds(framesCount * 1000d / SampleRate);

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        Dispose(true);
        GC.SuppressFinalize(this);
        isDisposed = true;
    }

    public virtual Task<Memory<float>> GetSamplesAsync(
        long startFrame,
        int maxFrames = int.MaxValue,
        CancellationToken cancellationToken = default
    )
    {
        ThrowIfNotInitialized();

        // We first check if we have the samples as floats, if not we deserialize them from bytes
        if (FloatFrames != null)
        {
            return Task.FromResult(GetFloatFramesSlice(startFrame, maxFrames));
        }

        var byteSlice = GetByteFramesSlice(startFrame, maxFrames);

        return Task.FromResult(SampleSerializer.Deserialize(byteSlice, BitsPerSample));
    }

    public virtual Task<Memory<byte>> GetFramesAsync(
        long startFrame,
        int maxFrames = int.MaxValue,
        CancellationToken cancellationToken = default
    )
    {
        // We first check if we have the samples as bytes, if not we serialize them from floats
        if (ByteFrames != null)
        {
            return Task.FromResult(GetByteFramesSlice(startFrame, maxFrames));
        }

        var slice = GetFloatFramesSlice(startFrame, maxFrames);

        return Task.FromResult(SampleSerializer.Serialize(slice, BitsPerSample));
    }

    public virtual Task<int> CopyFramesAsync(
        Memory<byte> destination,
        long startFrame,
        int maxFrames = int.MaxValue,
        CancellationToken cancellationToken = default
    )
    {
        if (ByteFrames != null)
        {
            var slice = GetByteFramesSlice(startFrame, maxFrames);

            slice.CopyTo(destination);
            var byteFrameCount = slice.Length / FrameSize;

            return Task.FromResult(byteFrameCount);
        }

        var floatSlice = GetFloatFramesSlice(startFrame, maxFrames);
        SampleSerializer.Serialize(floatSlice, destination, BitsPerSample);

        var frameCount = floatSlice.Length / ChannelCount;

        return Task.FromResult(frameCount);
    }

    public bool StoresFloats => FloatFrames != null;

    public bool StoresBytes => ByteFrames != null;

    ~BufferedMemoryAudioSource()
    {
        Dispose(false);
    }

    /// <summary>
    ///     Initializes the source with the specified header.
    /// </summary>
    /// <param name="header">The audio source header.</param>
    /// <exception cref="InvalidOperationException">Thrown when the source is already initialized.</exception>
    public virtual void Initialize(AudioSourceHeader header)
    {
        if (IsInitialized)
        {
            throw new InvalidOperationException("The source is already initialized.");
        }

        Header = header;
        IsInitialized = true;
    }

    /// <summary>
    ///     Adds frames to the source.
    /// </summary>
    /// <param name="frame">The frame of samples to add.</param>
    /// <exception cref="InvalidOperationException">Thrown when the source is not initialized.</exception>
    /// <exception cref="ArgumentException">Thrown when the frame size does not match the channels.</exception>
    public virtual void AddFrame(ReadOnlyMemory<float> frame)
    {
        ThrowIfNotInitialized();

        if (frame.Length != Header.Channels)
        {
            throw new ArgumentException(
                "The frame size does not match the channels.",
                nameof(frame)
            );
        }

        if (FloatFrames != null)
        {
            AddFrameToSamples(frame);
        }

        if (ByteFrames != null)
        {
            AddFrameToFrames(frame);
        }

        framesCount++;
    }

    /// <summary>
    ///     Adds frames to the source.
    /// </summary>
    /// <param name="frame">The frame buffer to add.</param>
    /// <exception cref="InvalidOperationException">Thrown when the source is not initialized.</exception>
    /// <exception cref="ArgumentException">Thrown when the frame size does not match the channels.</exception>
    public virtual void AddFrame(ReadOnlyMemory<byte> frame)
    {
        ThrowIfNotInitialized();
        if (frame.Length != Header.Channels * Header.BitsPerSample / 8)
        {
            throw new ArgumentException(
                "The frame size does not match the channels.",
                nameof(frame)
            );
        }

        if (FloatFrames != null)
        {
            AddFrameToSamples(frame);
        }

        if (ByteFrames != null)
        {
            AddFrameToFrames(frame);
        }

        framesCount++;
    }

    protected void ThrowIfNotInitialized()
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("The source is not initialized.");
        }
    }

    /// <summary>
    ///     Disposes the object.
    /// </summary>
    /// <param name="disposing">A value indicating whether the method is called from Dispose.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            FloatFrames = [];
            ByteFrames = [];
        }
    }

    private Memory<float> GetFloatFramesSlice(long startFrame, int maxFrames)
    {
        var startSample = (int)(startFrame * ChannelCount);
        var length = (int)(Math.Min(maxFrames, FramesCount - startFrame) * ChannelCount);

        return FloatFrames.AsMemory(startSample, length);
    }

    private Memory<byte> GetByteFramesSlice(long startFrame, int maxFrames)
    {
        var startByte = (int)(startFrame * FrameSize);
        var lengthBytes = (int)(Math.Min(maxFrames, FramesCount - startFrame) * FrameSize);

        return ByteFrames.AsMemory(startByte, lengthBytes);
    }

    private void AddFrameToFrames(ReadOnlyMemory<byte> frame)
    {
        if (ByteFrames!.Length <= FramesCount * FrameSize)
        {
            Array.Resize(ref ByteFrames, ByteFrames.Length * 2);
        }

        var startByte = (int)(FramesCount * FrameSize);
        var destinationMemory = ByteFrames.AsMemory(startByte);
        if (aggregationStrategy != null)
        {
            aggregationStrategy.Aggregate(frame, destinationMemory, BitsPerSample);
        }
        else
        {
            frame.Span.CopyTo(destinationMemory.Span);
        }
    }

    private void AddFrameToSamples(ReadOnlyMemory<byte> frame)
    {
        if (FloatFrames!.Length <= FramesCount * ChannelCount)
        {
            Array.Resize(ref FloatFrames, FloatFrames.Length * 2);
        }

        var destinationMemory = FloatFrames.AsMemory((int)(FramesCount * ChannelCount));

        if (aggregationStrategy != null)
        {
            aggregationStrategy.Aggregate(frame, destinationMemory, BitsPerSample);
        }
        else
        {
            SampleSerializer.Deserialize(
                frame,
                FloatFrames.AsMemory((int)(FramesCount * ChannelCount)),
                BitsPerSample
            );
        }
    }

    private void AddFrameToFrames(ReadOnlyMemory<float> frame)
    {
        if (ByteFrames!.Length <= FramesCount * FrameSize)
        {
            Array.Resize(ref ByteFrames, ByteFrames.Length * 2);
        }

        var startByte = (int)(FramesCount * FrameSize);
        var destinationMemory = ByteFrames.AsMemory(startByte);
        if (aggregationStrategy != null)
        {
            aggregationStrategy.Aggregate(frame, destinationMemory, BitsPerSample);
        }
        else
        {
            SampleSerializer.Serialize(frame, ByteFrames.AsMemory(startByte), BitsPerSample);
        }
    }

    private void AddFrameToSamples(ReadOnlyMemory<float> frame)
    {
        if (FloatFrames!.Length <= FramesCount * ChannelCount)
        {
            Array.Resize(ref FloatFrames, FloatFrames.Length * 2);
        }

        var destinationMemory = FloatFrames.AsMemory((int)(FramesCount * ChannelCount));
        if (aggregationStrategy != null)
        {
            aggregationStrategy.Aggregate(frame, destinationMemory);
        }
        else
        {
            frame.Span.CopyTo(destinationMemory.Span);
        }
    }
}
