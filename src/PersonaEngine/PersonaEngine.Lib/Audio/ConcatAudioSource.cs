namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Represents an audio source that concatenates multiple audio sources
/// </summary>
public sealed class ConcatAudioSource : IAudioSource
{
    private readonly IAudioSource[] sources;

    public ConcatAudioSource(IAudioSource[] sources, IReadOnlyDictionary<string, string> metadata)
    {
        this.sources = sources;
        Metadata = metadata;
        if (sources.Length == 0)
        {
            throw new ArgumentException("At least one source must be provided.", nameof(sources));
        }

        var duration = TimeSpan.Zero;
        var sampleRate = sources[0].SampleRate;
        var framesCount = 0L;
        var channelCount = sources[0].ChannelCount;
        var bitsPerSample = sources[0].BitsPerSample;
        var totalDuration = TimeSpan.Zero;

        for (var i = 0; i < sources.Length; i++)
        {
            duration += sources[i].Duration;
            totalDuration += sources[i].TotalDuration;
            framesCount += sources[i].FramesCount;
            if (channelCount != sources[i].ChannelCount)
            {
                throw new ArgumentException(
                    "All sources must have the same channel count.",
                    nameof(sources)
                );
            }

            if (sampleRate != sources[i].SampleRate)
            {
                throw new ArgumentException(
                    "All sources must have the same sample rate.",
                    nameof(sources)
                );
            }

            if (bitsPerSample != sources[i].BitsPerSample)
            {
                throw new ArgumentException(
                    "All sources must have the same bits per sample.",
                    nameof(sources)
                );
            }
        }

        Duration = duration;
        SampleRate = sampleRate;
        FramesCount = framesCount;
        ChannelCount = channelCount;
        BitsPerSample = bitsPerSample;
        TotalDuration = totalDuration;
    }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    public TimeSpan Duration { get; }

    public TimeSpan TotalDuration { get; }

    public uint SampleRate { get; }

    public long FramesCount { get; }

    public ushort ChannelCount { get; }

    public bool IsInitialized => true;

    public ushort BitsPerSample { get; }

    public void Dispose()
    {
        foreach (var source in sources)
        {
            source.Dispose();
        }
    }

    public async Task<Memory<byte>> GetFramesAsync(
        long startFrame,
        int maxFrames = int.MaxValue,
        CancellationToken cancellationToken = default
    )
    {
        var slices = await GetMemorySlicesAsync(
            (source, offset, frames, token) => source.GetFramesAsync(offset, frames, token),
            startFrame,
            maxFrames,
            cancellationToken
        );

        return MergeMemorySlices(slices);
    }

    public async Task<Memory<float>> GetSamplesAsync(
        long startFrame,
        int maxFrames = int.MaxValue,
        CancellationToken cancellationToken = default
    )
    {
        var slices = await GetMemorySlicesAsync(
            (source, offset, frames, token) => source.GetSamplesAsync(offset, frames, token),
            startFrame,
            maxFrames,
            cancellationToken
        );

        return MergeMemorySlices(slices);
    }

    public async Task<int> CopyFramesAsync(
        Memory<byte> destination,
        long startFrame,
        int maxFrames = int.MaxValue,
        CancellationToken cancellationToken = default
    )
    {
        var slices = await GetMemorySlicesAsync(
            (source, offset, frames, token) => source.GetFramesAsync(offset, frames, token),
            startFrame,
            maxFrames,
            cancellationToken
        );

        CopySlices(slices, destination);
        long length = slices.Sum(slice => slice.Length);

        return (int)(length / BitsPerSample * 8 / ChannelCount);
    }

    private async Task<List<Memory<T>>> GetMemorySlicesAsync<T>(
        Func<IAudioSource, long, int, CancellationToken, Task<Memory<T>>> getMemoryFunc,
        long startFrame,
        int maxFrames,
        CancellationToken cancellationToken
    )
    {
        var result = new List<Memory<T>>();
        var framesToRead = maxFrames;
        var offset = startFrame;

        foreach (var source in sources)
        {
            if (offset >= source.FramesCount)
            {
                offset -= source.FramesCount;

                continue;
            }

            var framesFromThisSource = Math.Min(framesToRead, (int)(source.FramesCount - offset));
            var memory = await getMemoryFunc(
                source,
                offset,
                framesFromThisSource,
                cancellationToken
            );

            result.Add(memory);

            framesToRead -= framesFromThisSource;
            offset = 0;

            if (framesToRead <= 0)
            {
                break;
            }
        }

        return result;
    }

    private static Memory<T> MergeMemorySlices<T>(List<Memory<T>> slices)
    {
        if (slices.Count == 1)
        {
            return slices[0]; // If all frames/samples are from a single source, return directly
        }

        var totalSize = slices.Sum(slice => slice.Length);
        var merged = new T[totalSize];
        CopySlices(slices, merged);

        return merged.AsMemory();
    }

    private static void CopySlices<T>(List<Memory<T>> slices, Memory<T> destination)
    {
        var position = 0;

        foreach (var slice in slices)
        {
            slice.CopyTo(destination.Slice(position));
            position += slice.Length;
        }
    }
}
