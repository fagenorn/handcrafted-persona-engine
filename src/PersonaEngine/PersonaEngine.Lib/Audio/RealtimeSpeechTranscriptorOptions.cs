using PersonaEngine.Lib.ASR.Transcriber;

namespace PersonaEngine.Lib.Audio;

public record RealtimeSpeechTranscriptorOptions : SpeechTranscriptorOptions
{
    /// <summary>
    ///     Gets or sets a value indicating whether to include speech recognizing events.
    /// </summary>
    /// <remarks>
    ///     This events are emitted before the segment is complete and are not final.
    /// </remarks>
    public bool IncludeSpeechRecogizingEvents { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether to autodetect the language only once at startup and use it for all the
    ///     segments, instead of autodetecting it for each segment.
    /// </summary>
    public bool AutodetectLanguageOnce { get; set; }
}
