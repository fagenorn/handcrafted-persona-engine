namespace PersonaEngine.Lib.Audio;

public readonly struct SliceAudioSource : IAudioSource
{
    private readonly long frameStart;

    private readonly int maxSliceFrames;

    private readonly IAudioSource audioSource;

    private readonly TimeSpan startTime;

    private readonly TimeSpan maxDuration;

    public uint SampleRate { get; }

    /// <summary>
    ///     Gets the total number of frames in the stream
    /// </summary>
    /// <remarks>
    ///     This can be more than actual number of frames in the source if the source is not big enough.
    /// </remarks>
    public long FramesCount => (long)(Duration.TotalMilliseconds * SampleRate / 1000);

    public ushort ChannelCount { get; }

    public bool IsInitialized => true;

    public ushort BitsPerSample { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    public TimeSpan Duration
    {
        get
        {
            var maxSourceDuration = audioSource.TotalDuration - startTime;

            return maxSourceDuration > maxDuration ? maxDuration : maxSourceDuration;
        }
    }

    public TimeSpan TotalDuration => Duration;

    /// <summary>
    ///     Creates a slice of an audio source (without copying the audio data).
    /// </summary>
    public SliceAudioSource(IAudioSource audioSource, TimeSpan startTime, TimeSpan maxDuration)
    {
        frameStart = (long)(startTime.TotalMilliseconds * audioSource.SampleRate / 1000);
        maxSliceFrames = (int)(maxDuration.TotalMilliseconds * audioSource.SampleRate / 1000);
        SampleRate = audioSource.SampleRate;

        if (startTime >= audioSource.Duration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startTime),
                $"The start time is beyond the end of the audio source. Start time: {startTime}, Source Duration: {audioSource.Duration}"
            );
        }

        ChannelCount = audioSource.ChannelCount;
        BitsPerSample = audioSource.BitsPerSample;
        this.audioSource = audioSource;
        this.maxDuration = maxDuration;
        Metadata = audioSource.Metadata;
        this.startTime = startTime;
    }

    public void Dispose() { }

    public Task<Memory<byte>> GetFramesAsync(
        long startFrame,
        int maxFrames = int.MaxValue,
        CancellationToken cancellationToken = default
    )
    {
        var adjustedMax = (int)Math.Min(maxFrames, maxSliceFrames - startFrame);

        return audioSource.GetFramesAsync(startFrame + frameStart, adjustedMax, cancellationToken);
    }

    public Task<Memory<float>> GetSamplesAsync(
        long startFrame,
        int maxFrames = int.MaxValue,
        CancellationToken cancellationToken = default
    )
    {
        var adjustedMax = (int)Math.Min(maxFrames, maxSliceFrames - startFrame);

        return audioSource.GetSamplesAsync(startFrame + frameStart, adjustedMax, cancellationToken);
    }

    public Task<int> CopyFramesAsync(
        Memory<byte> destination,
        long startFrame,
        int maxFrames = int.MaxValue,
        CancellationToken cancellationToken = default
    )
    {
        var adjustedMax = (int)Math.Min(maxFrames, maxSliceFrames - startFrame);

        return audioSource.CopyFramesAsync(
            destination,
            startFrame + frameStart,
            adjustedMax,
            cancellationToken
        );
    }
}
