using System.Globalization;

namespace PersonaEngine.Lib.ASR.Transcriber;

public class TranscriptSegment
{
    public IReadOnlyDictionary<string, string> Metadata { get; init; }

    public string Text { get; set; } = string.Empty;

    public TimeSpan StartTime { get; set; }

    public TimeSpan Duration { get; set; }

    public float? ConfidenceLevel { get; set; }

    public CultureInfo? Language { get; set; }

    public IList<TranscriptToken>? Tokens { get; set; }
}
