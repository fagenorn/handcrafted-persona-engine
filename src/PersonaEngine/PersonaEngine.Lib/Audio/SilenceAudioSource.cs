using System.Buffers;

namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Represents an audio source that stores a continuous memory with only silence and returns it when needed.
/// </summary>
public sealed class SilenceAudioSource : IAudioSource
{
    private readonly Lazy<byte[]> byteSilence;

    private readonly Lazy<float[]> floatSilence;

    private readonly bool useMemoryPool;

    public SilenceAudioSource(
        TimeSpan duration,
        uint sampleRate,
        IReadOnlyDictionary<string, string> metadata,
        ushort channelCount = 1,
        ushort bitsPerSample = 16,
        bool useMemoryPool = true
    )
    {
        SampleRate = sampleRate;
        Metadata = metadata;
        ChannelCount = channelCount;
        BitsPerSample = bitsPerSample;
        this.useMemoryPool = useMemoryPool;
        Duration = duration;
        FramesCount = (long)(duration.TotalSeconds * sampleRate);

        byteSilence = new Lazy<byte[]>(GenerateByteSilence);
        floatSilence = new Lazy<float[]>(GenerateFloatSilence);
    }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    public TimeSpan Duration { get; }

    public TimeSpan TotalDuration => Duration;

    public uint SampleRate { get; }

    public long FramesCount { get; }

    public ushort ChannelCount { get; }

    public bool IsInitialized => true;

    public ushort BitsPerSample { get; }

    public void Dispose()
    {
        if (useMemoryPool)
        {
            // We don't want to clear this as it is silence anyway (no user data to clear)
            if (byteSilence.IsValueCreated)
            {
                ArrayPool<byte>.Shared.Return(byteSilence.Value);
            }

            if (floatSilence.IsValueCreated)
            {
                ArrayPool<float>.Shared.Return(floatSilence.Value, true);
            }
        }
    }

    public Task<Memory<byte>> GetFramesAsync(
        long startFrame,
        int maxFrames = int.MaxValue,
        CancellationToken cancellationToken = default
    )
    {
        var frameSize = ChannelCount * BitsPerSample / 8;
        var startIndex = (int)(startFrame * frameSize);
        var lengthBytes = (int)(Math.Min(maxFrames, FramesCount - startFrame) * frameSize);

        var slice = byteSilence.Value.AsMemory(startIndex, lengthBytes);

        return Task.FromResult(slice);
    }

    public Task<Memory<float>> GetSamplesAsync(
        long startFrame,
        int maxFrames = int.MaxValue,
        CancellationToken cancellationToken = default
    )
    {
        var startIndex = (int)(startFrame * ChannelCount);
        var lengthSamples = (int)(Math.Min(maxFrames, FramesCount - startFrame) * ChannelCount);

        var slice = floatSilence.Value.AsMemory(startIndex, lengthSamples);

        return Task.FromResult(slice);
    }

    public Task<int> CopyFramesAsync(
        Memory<byte> destination,
        long startFrame,
        int maxFrames = int.MaxValue,
        CancellationToken cancellationToken = default
    )
    {
        var frameSize = ChannelCount * BitsPerSample / 8;
        var startIndex = (int)(startFrame * frameSize);
        var lengthBytes = (int)(Math.Min(maxFrames, FramesCount - startFrame) * frameSize);

        var slice = byteSilence.Value.AsMemory(startIndex, lengthBytes);
        slice.CopyTo(destination);
        var frames = lengthBytes / frameSize;

        return Task.FromResult(frames);
    }

    private byte[] GenerateByteSilence()
    {
        var frameSize = ChannelCount * BitsPerSample / 8;
        var totalBytes = FramesCount * frameSize;

        var silence = useMemoryPool
            ? ArrayPool<byte>.Shared.Rent((int)totalBytes)
            : new byte[totalBytes];

        // 8-bit PCM silence is centered at 128, not 0
        if (BitsPerSample == 8)
        {
            Array.Fill<byte>(silence, 128, 0, (int)totalBytes);
        }
        else
        {
            Array.Clear(silence, 0, (int)totalBytes); // Zero out for non-8-bit audio
        }

        return silence;
    }

    private float[] GenerateFloatSilence()
    {
        var totalSamples = FramesCount * ChannelCount;
        var silence = useMemoryPool
            ? ArrayPool<float>.Shared.Rent((int)totalSamples)
            : new float[totalSamples];

        Array.Clear(silence, 0, (int)totalSamples); // Silence is zeroed-out memory

        return silence;
    }
}
