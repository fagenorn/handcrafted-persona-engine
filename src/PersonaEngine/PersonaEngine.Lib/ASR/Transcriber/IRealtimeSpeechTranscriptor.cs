using PersonaEngine.Lib.Audio;

namespace PersonaEngine.Lib.ASR.Transcriber;

public interface IRealtimeSpeechTranscriptor
{
    IAsyncEnumerable<IRealtimeRecognitionEvent> TranscribeAsync(
        IAwaitableAudioSource source,
        CancellationToken cancellationToken = default
    );
}
