namespace PersonaEngine.Lib.Music.AudioSeperator;

public interface IAudioSourceSeparator : IAsyncDisposable
{
    int SampleRate { get; }
    int Channels { get; }
    int ChunkSize { get; }
    int NumOverlap { get; }
    int HopSize { get; }

    /// <summary>Initialize any heavy resources (e.g., ONNX session). Safe to call once.</summary>
    ValueTask InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Stream in audio blocks (arbitrary sizes) and receive separated blocks back.
    /// The implementation will buffer and emit HopSize frames per step after the pipeline is warm.
    /// </summary>
    IAsyncEnumerable<SeparationBlock> ProcessAsync(
        IAsyncEnumerable<AudioBlock> input,
        CancellationToken ct = default
    );

    /// <summary>Reset internal state to start a new stream (does not dispose the session).</summary>
    void Reset();
}

public readonly record struct AudioBlock(ReadOnlyMemory<float> Interleaved);

public readonly record struct SeparationBlock(
    ReadOnlyMemory<float> Target,
    ReadOnlyMemory<float> Instrumental
);
