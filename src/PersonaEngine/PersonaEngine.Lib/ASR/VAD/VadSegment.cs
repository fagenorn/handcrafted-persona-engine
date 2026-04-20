namespace PersonaEngine.Lib.ASR.VAD;

/// <summary>
///     Represents a voice activity detection segment.
/// </summary>
public class VadSegment
{
    /// <summary>
    ///     Gets or sets the start time of the segment.
    /// </summary>
    public TimeSpan StartTime { get; set; }

    /// <summary>
    ///     Gets or sets the duration of the segment.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether this segment is the last one in the audio stream and can be incomplete.
    /// </summary>
    public bool IsIncomplete { get; set; }
}
