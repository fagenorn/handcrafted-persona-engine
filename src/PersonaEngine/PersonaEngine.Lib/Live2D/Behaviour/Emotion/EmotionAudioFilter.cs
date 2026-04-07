using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.TTS.Synthesis.Audio;

namespace PersonaEngine.Lib.Live2D.Behaviour.Emotion;

/// <summary>
///     Audio filter that attaches emotion data to segments during processing
/// </summary>
public class EmotionAudioFilter : IAudioFilter
{
    private readonly IEmotionService _emotionService;

    private readonly ILogger<EmotionAudioFilter> _logger;

    public EmotionAudioFilter(IEmotionService emotionService, ILoggerFactory loggerFactory)
    {
        _emotionService = emotionService ?? throw new ArgumentNullException(nameof(emotionService));
        _logger =
            loggerFactory?.CreateLogger<EmotionAudioFilter>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <summary>
    ///     Priority of the filter (should run after other filters)
    /// </summary>
    public int Priority => -100;

    /// <summary>
    ///     Processes the audio segment to attach emotion data
    /// </summary>
    public void Process(AudioSegment segment)
    {
        // No modification to the audio data is needed
        // This filter is just here to hook into the audio processing pipeline
        // and could be used to perform any additional emotion-related processing if needed

        var emotions = _emotionService.GetEmotions(segment.Id);
        if (emotions.Any())
        {
            _logger.LogDebug(
                "Audio segment {SegmentId} has {Count} emotions",
                segment.Id,
                emotions.Count
            );

            // The emotions are already registered with the emotion service
            // We could attach them directly to the segment if needed via a custom extension method
            // or simply document that they should be retrieved via the service
        }
    }
}
