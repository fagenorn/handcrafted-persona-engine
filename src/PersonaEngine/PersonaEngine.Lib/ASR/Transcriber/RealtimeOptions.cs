namespace PersonaEngine.Lib.ASR.Transcriber;

public class RealtimeOptions
{
    /// <summary>
    ///     Represents the length of the padding segmetents that will be appended to each sement detected by the VAD.
    /// </summary>
    /// <example>
    ///     If the VAD detects a segment of 1200ms starting at offset 200ms, and the <see cref="PaddingDuration" /> is 125ms,
    ///     125ms of the original stream will be appended at the beginning and 125ms at the end of the segment.
    /// </example>
    /// <remarks>
    ///     If the original stream is not long enough to append the padding segments, the padding won't be applied.
    /// </remarks>
    public TimeSpan PaddingDuration { get; set; } = TimeSpan.FromMilliseconds(125);

    /// <summary>
    ///     Represents the minimum length of a segment that will be processed by the transcriptor, if the segment is shorter
    ///     than this value it will be ignored.
    /// </summary>
    public TimeSpan MinTranscriptDuration { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    ///     Represents the minimum length of a segment and if it is shorter than this value it will be padded with silence.
    /// </summary>
    /// <example>
    ///     If the segment is 700ms, and the <see cref="MinDurationWithPadding" /> is 1100ms, the segment will be padded with
    ///     400ms of silence (200 ms at the beginning and 200ms at the end).
    /// </example>
    public TimeSpan MinDurationWithPadding { get; set; } = TimeSpan.FromMilliseconds(1100);

    /// <summary>
    ///     If set to true, the recognized segments will be concatenated to form the prompt for newer recognition.
    /// </summary>
    public bool ConcatenateSegmentsToPrompt { get; set; }

    /// <summary>
    ///     Represents the interval at which the transcriptor will process the audio stream.
    /// </summary>
    public TimeSpan ProcessingInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    ///     Represents the interval at which the transcriptor will discard silence audio segments.
    /// </summary>
    public TimeSpan SilenceDiscardInterval { get; set; } = TimeSpan.FromSeconds(5);
}
