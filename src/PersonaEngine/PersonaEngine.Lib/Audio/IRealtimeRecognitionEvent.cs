using PersonaEngine.Lib.ASR.Transcriber;

namespace PersonaEngine.Lib.Audio;

public interface IRealtimeRecognitionEvent { }

public interface IRealtimeTranscriptionSegment : IRealtimeRecognitionEvent
{
    public TranscriptSegment Segment { get; }
}

public class RealtimeSessionStarted(string sessionId) : IRealtimeRecognitionEvent
{
    public string SessionId { get; } = sessionId;
}

public class RealtimeSessionStopped(string sessionId) : IRealtimeRecognitionEvent
{
    public object SessionId { get; } = sessionId;
}

public class RealtimeSessionCanceled(string sessionId) : IRealtimeRecognitionEvent
{
    public object SessionId { get; } = sessionId;
}

public record RealtimeSegmentRecognizing(
    TranscriptSegment Segment,
    string SessionId,
    TimeSpan ProcessingDuration
) : IRealtimeTranscriptionSegment
{
    public TranscriptSegment Segment { get; } = Segment;

    public string SessionId { get; } = SessionId;
}

public record RealtimeSegmentRecognized(
    TranscriptSegment Segment,
    string SessionId,
    TimeSpan ProcessingDuration
) : IRealtimeTranscriptionSegment
{
    public TranscriptSegment Segment { get; } = Segment;

    public string SessionId { get; } = SessionId;
}
