using System.Buffers;

namespace PersonaEngine.Lib.TTS.Synthesis.Alignment;

/// <summary>
///     Provides word-level timing by aligning known text against audio.
///     Engine-agnostic — can be used with any TTS backend that produces PCM audio.
/// </summary>
public interface IForcedAligner : IDisposable
{
    /// <summary>
    ///     Aligns known text against audio, returning word-level timings.
    ///     The caller must dispose the result to return the pooled buffer.
    /// </summary>
    /// <param name="audio">Mono float32 PCM audio.</param>
    /// <param name="text">The exact text that was synthesized.</param>
    /// <param name="sampleRate">Sample rate of the input audio.</param>
    AlignmentResult Align(ReadOnlySpan<float> audio, string text, int sampleRate);

    /// <summary>
    ///     Windowed variant: aligns only a recent audio window against remaining transcript words.
    ///     Skips already-confirmed words for O(window) cost instead of O(total_audio).
    /// </summary>
    /// <param name="audioWindow">Audio window starting from <paramref name="windowStartTime"/>.</param>
    /// <param name="remainingText">Transcript text for words not yet confirmed.</param>
    /// <param name="sampleRate">Sample rate of the audio window.</param>
    /// <param name="windowStartTime">Absolute start time of the audio window in seconds.</param>
    AlignmentResult AlignSpokenWindowed(
        ReadOnlySpan<float> audioWindow,
        string remainingText,
        int sampleRate,
        double windowStartTime
    );
}

/// <summary>
///     Word-level timing produced by forced alignment.
/// </summary>
public readonly record struct WordTiming(
    string Word,
    TimeSpan StartTime,
    TimeSpan EndTime,
    float Confidence
)
{
    public TimeSpan Duration => EndTime - StartTime;
}

/// <summary>
///     Pooled alignment result. Dispose to return the buffer to the pool.
/// </summary>
public readonly struct AlignmentResult : IDisposable
{
    private readonly WordTiming[]? _buffer;

    public static AlignmentResult Empty => new(null, 0);

    public AlignmentResult(WordTiming[]? buffer, int count)
    {
        _buffer = buffer;
        Count = count;
    }

    public int Count { get; }

    public ReadOnlySpan<WordTiming> Timings => _buffer.AsSpan(0, Count);

    public void Dispose()
    {
        if (_buffer is not null)
        {
            ArrayPool<WordTiming>.Shared.Return(_buffer);
        }
    }
}
