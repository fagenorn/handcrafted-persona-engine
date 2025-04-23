namespace PersonaEngine.Lib.Audio;

public interface IAudioSource : IDisposable
{
    IReadOnlyDictionary<string, string> Metadata { get; }

    TimeSpan Duration { get; }

    TimeSpan TotalDuration { get; }

    uint SampleRate { get; }

    long FramesCount { get; }

    ushort ChannelCount { get; }

    bool IsInitialized { get; }

    ushort BitsPerSample { get; }

    Task<Memory<float>> GetSamplesAsync(
        long startFrame,
        int maxFrames = int.MaxValue,
        CancellationToken cancellationToken = default
    );

    Task<Memory<byte>> GetFramesAsync(
        long startFrame,
        int maxFrames = int.MaxValue,
        CancellationToken cancellationToken = default
    );

    Task<int> CopyFramesAsync(
        Memory<byte> destination,
        long startFrame,
        int maxFrames = int.MaxValue,
        CancellationToken cancellationToken = default
    );
}
