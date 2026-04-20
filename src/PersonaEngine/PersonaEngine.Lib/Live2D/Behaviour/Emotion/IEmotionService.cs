namespace PersonaEngine.Lib.Live2D.Behaviour.Emotion;

/// <summary>
/// Service that tracks emotion timings for audio segments
/// </summary>
public interface IEmotionService
{
    /// <summary>
    /// Associates emotion timings with an audio segment
    /// </summary>
    void RegisterEmotions(Guid segmentId, IReadOnlyList<EmotionTiming> emotions);

    /// <summary>
    /// Retrieves emotion timings for an audio segment
    /// </summary>
    IReadOnlyList<EmotionTiming> GetEmotions(Guid segmentId);
}
