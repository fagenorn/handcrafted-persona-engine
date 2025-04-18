using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.LLM;
using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.Live2D.Behaviour.Emotion;

/// <summary>
///     Processor for extracting emotion tags from text before TTS synthesis
/// </summary>
public class EmotionProcessor(IEmotionService emotionService, ILoggerFactory loggerFactory) : ITextFilter
{
    private const string MetadataKey = "EmotionMarkers";

    private const string MarkerPrefix = "__EM";

    private const string MarkerSuffix = "__";

    private static readonly Regex EmotionTagRegex = new(@"\[EMOTION:(.*?)\]",
                                                        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ILogger<EmotionProcessor> _logger = loggerFactory.CreateLogger<EmotionProcessor>();

    public int Priority => 100;

    public ValueTask<TextFilterResult> ProcessAsync(string text, CancellationToken cancellationToken = default)
    {
        if ( string.IsNullOrEmpty(text) )
        {
            return ValueTask.FromResult(new TextFilterResult { ProcessedText = text });
        }

        cancellationToken.ThrowIfCancellationRequested();

        var markers              = new List<EmotionMarkerInfo>();
        var processedTextBuilder = new StringBuilder();
        var lastIndex            = 0;
        var markerIndex          = 0;

        var matches = EmotionTagRegex.Matches(text);
        if ( matches.Count == 0 )
        {
            _logger.LogTrace("No emotion tags found in the input text.");

            return ValueTask.FromResult(new TextFilterResult { ProcessedText = text });
        }

        _logger.LogDebug("Found {Count} potential emotion tags.", matches.Count);

        foreach ( Match match in matches )
        {
            cancellationToken.ThrowIfCancellationRequested();

            processedTextBuilder.Append(text, lastIndex, match.Index - lastIndex);
            var emotionValue = match.Groups[1].Value;
            if ( string.IsNullOrWhiteSpace(emotionValue) )
            {
                _logger.LogWarning("Found emotion tag with empty value at index {Index}. Skipping.", match.Index);
                processedTextBuilder.Append(match.Value);
                lastIndex = match.Index + match.Length;

                continue;
            }

            // e.g., [__EM0__](//), [__EM1__](//) This causes the phonemizer to see it as a feature and ignore it
            var markerId    = $"{MarkerPrefix}{markerIndex++}{MarkerSuffix}";
            var markerToken = $"[{markerId}](//)";
            processedTextBuilder.Append(markerToken);

            var markerInfo = new EmotionMarkerInfo { MarkerId = markerId, Emotion = emotionValue };
            markers.Add(markerInfo);

            _logger.LogTrace("Replaced tag '{Tag}' with marker '{Marker}' for emotion '{Emotion}'.",
                             match.Value, markerId, emotionValue);

            lastIndex = match.Index + match.Length;
        }

        processedTextBuilder.Append(text, lastIndex, text.Length - lastIndex);
        var processedText = processedTextBuilder.ToString();
        _logger.LogDebug("Processed text length: {Length}. Original length: {OriginalLength}.",
                         processedText.Length, text.Length);

        var metadata = new Dictionary<string, object>();
        if ( markers.Count != 0 )
        {
            metadata[MetadataKey] = markers;
        }

        var result = new TextFilterResult { ProcessedText = processedText, Metadata = metadata };

        return ValueTask.FromResult(result);
    }

    public ValueTask PostProcessAsync(TextFilterResult textFilterResult, AudioSegment segment, CancellationToken cancellationToken = default)
    {
        if ( !segment.Tokens.Any() ||
             !textFilterResult.Metadata.TryGetValue(MetadataKey, out var markersObj) ||
             markersObj is not List<EmotionMarkerInfo> markers || markers.Count == 0 )
        {
            _logger.LogTrace("No emotion markers found in metadata or segment/tokens are missing/empty for segment Id {SegmentId}. Skipping post-processing.", segment?.Id);

            return ValueTask.CompletedTask;
        }

        _logger.LogDebug("Starting emotion post-processing for segment Id {SegmentId} with {MarkerCount} markers.", segment.Id, markers.Count);

        cancellationToken.ThrowIfCancellationRequested();

        var emotionTimings   = new List<EmotionTiming>();
        var processedMarkers = new HashSet<string>();
        var allMarkersFound  = false;

        var markerDict = markers.ToDictionary(m => m.MarkerId, m => m.Emotion);

        foreach ( var token in segment.Tokens )
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach ( var (markerId, emotion) in markerDict )
            {
                if ( processedMarkers.Contains(markerId) || !token.Text.Contains(markerId) )
                {
                    continue;
                }

                // Usually okay, the only case wher StartTS is 0 is if it's the first token.
                var timestamp = token.StartTs ?? 0;

                emotionTimings.Add(new EmotionTiming { Timestamp = timestamp, Emotion = emotion });
                processedMarkers.Add(markerId);
                token.Text = string.Empty;

                _logger.LogDebug("Mapped emotion '{Emotion}' (from marker '{Marker}') to timestamp {Timestamp:F2}s based on token '{TokenText}'.",
                                 emotion, markerId, timestamp, token.Text);

                if ( processedMarkers.Count == markers.Count )
                {
                    _logger.LogDebug("All {Count} markers have been processed. Exiting token scan early.", markers.Count);
                    allMarkersFound = true;

                    break;
                }
            }

            if ( allMarkersFound )
            {
                break;
            }
        }

        if ( emotionTimings.Count != 0 )
        {
            _logger.LogInformation("Registering {Count} timed emotions for segment Id {SegmentId}.", emotionTimings.Count, segment.Id);
            try
            {
                emotionService.RegisterEmotions(segment.Id, emotionTimings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering emotions for segment Id {SegmentId}.", segment.Id);
            }
        }
        else
        {
            _logger.LogWarning("Could not map any emotion markers to timestamps for segment Id {SegmentId}. This might happen if markers were removed by later filters or TTS engine issues.", segment.Id);
        }

        if ( processedMarkers.Count >= markers.Count )
        {
            return ValueTask.CompletedTask;
        }

        {
            var unprocessed = markers.Where(m => !processedMarkers.Contains(m.MarkerId)).Select(m => m.MarkerId);
            _logger.LogWarning("The following emotion markers were found in ProcessAsync but not located in the final tokens for segment Id {SegmentId}: {UnprocessedMarkers}",
                               segment.Id, string.Join(", ", unprocessed));
        }

        return ValueTask.CompletedTask;
    }
}