using Microsoft.Extensions.Logging;

namespace PersonaEngine.Lib.Live2D.Behaviour.Emotion;

/// <summary>
///     Implementation of the emotion tracking service
/// </summary>
public class EmotionService : IEmotionService
{
    private readonly Dictionary<Guid, IReadOnlyList<EmotionTiming>> _emotionMap = new();

    private readonly ILogger<EmotionService> _logger;

    public EmotionService(ILogger<EmotionService> logger)
    {
        _logger = logger;
    }

    public void RegisterEmotions(Guid segmentId, IReadOnlyList<EmotionTiming> emotions)
    {
        if (_emotionMap.Count > 100)
        {
            _emotionMap.Clear();
            _logger.LogWarning("Emotion map cleared due to size limit");
        }

        if (emotions is not { Count: > 0 })
        {
            return;
        }

        _emotionMap[segmentId] = emotions;
        _logger.LogDebug(
            "Registered {Count} emotions for segment {SegmentId}",
            emotions.Count,
            segmentId
        );
    }

    public IReadOnlyList<EmotionTiming> GetEmotions(Guid segmentId)
    {
        if (_emotionMap.TryGetValue(segmentId, out var emotions))
        {
            return emotions;
        }

        return Array.Empty<EmotionTiming>();
    }
}
