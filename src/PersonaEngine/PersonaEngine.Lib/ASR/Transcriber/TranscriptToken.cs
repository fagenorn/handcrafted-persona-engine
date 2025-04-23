namespace PersonaEngine.Lib.ASR.Transcriber;

public class TranscriptToken
{
    public float? ConfidenceLog;

    public string? Text;

    public object? Id { get; set; }

    public float? Confidence { get; set; }

    public float TimestampConfidence { get; set; }

    public float TimestampConfidenceSum { get; set; }

    public TimeSpan StartTime { get; set; }

    public TimeSpan Duration { get; set; }

    public long DtwTimestamp { get; set; }
}
