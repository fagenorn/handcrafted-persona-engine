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


public class RealtimeSegmentRecognizing(TranscriptSegment segment, string sessionId) : IRealtimeTranscriptionSegment
{
    public TranscriptSegment Segment { get; } = segment;

    public string SessionId { get; } = sessionId;
}

public class RealtimeSegmentRecognized(TranscriptSegment segment, string sessionId) : IRealtimeTranscriptionSegment
{
    public TranscriptSegment Segment { get; } = segment;

    public string SessionId { get; } = sessionId;
}