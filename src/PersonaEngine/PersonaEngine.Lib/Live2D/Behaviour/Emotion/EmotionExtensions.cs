using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.Live2D.Behaviour.Emotion;

/// <summary>
///     Extension methods for accessing emotion data in audio segments
/// </summary>
public static class EmotionExtensions
{
    /// <summary>
    ///     Gets the emotions associated with an audio segment
    /// </summary>
    public static IReadOnlyList<EmotionTiming> GetEmotions(
        this AudioSegment segment,
        IEmotionService emotionService
    )
    {
        return emotionService.GetEmotions(segment.Id);
    }
}
