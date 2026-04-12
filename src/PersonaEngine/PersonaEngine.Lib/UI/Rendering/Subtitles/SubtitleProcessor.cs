using System.Text;
using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.UI.Rendering.Subtitles;

/// <summary>
///     Processes raw AudioSegments into structured SubtitleSegments containing lines and words
///     with calculated timing and layout information.
/// </summary>
public class SubtitleProcessor(TextMeasurer textMeasurer, float defaultWordDuration = 0.3f)
{
    private readonly float _defaultWordDuration = Math.Max(0.01f, defaultWordDuration);

    private readonly StringBuilder _textBuilder = new();

    public SubtitleSegment ProcessSegment(
        AudioSegment? audioSegment,
        float segmentAbsoluteStartTime
    )
    {
        if (audioSegment == null)
        {
            return new SubtitleSegment(segmentAbsoluteStartTime, string.Empty);
        }

        if (audioSegment.Tokens.Count == 0)
        {
            return new SubtitleSegment(segmentAbsoluteStartTime, string.Empty);
        }

        _textBuilder.Clear();
        foreach (var token in audioSegment.Tokens)
        {
            _textBuilder.Append(token.Text);
            _textBuilder.Append(token.Whitespace);
        }

        var processedSegment = new SubtitleSegment(
            segmentAbsoluteStartTime,
            _textBuilder.ToString()
        );
        var currentLine = new SubtitleLine(0, 0);
        var currentLineWidth = 0f;
        var cumulativeTimeOffset = 0f;

        for (var i = 0; i < audioSegment.Tokens.Count; i++)
        {
            var token = audioSegment.Tokens[i];
            var wordText = token.Text + token.Whitespace;
            var wordSize = textMeasurer.MeasureText(wordText);

            if (currentLineWidth > 0 && currentLineWidth + wordSize.X > textMeasurer.AvailableWidth)
            {
                processedSegment.AddLine(currentLine);
                currentLine = new SubtitleLine(processedSegment.Lines.Count, 0);
                currentLineWidth = 0f;
            }

            var (wordStartTimeOffset, wordDuration) = CalculateWordTiming(
                audioSegment,
                i,
                cumulativeTimeOffset
            );

            cumulativeTimeOffset = wordStartTimeOffset + wordDuration;

            var wordInfo = new SubtitleWordInfo
            {
                Text = wordText,
                Size = wordSize,
                AbsoluteStartTime = segmentAbsoluteStartTime + wordStartTimeOffset,
                Duration = wordDuration,
            };

            currentLine.AddWord(wordInfo);
            currentLineWidth += wordSize.X;
        }

        if (currentLine.Words.Count > 0)
        {
            processedSegment.AddLine(currentLine);
        }

        return processedSegment;
    }

    /// <summary>
    ///     Appends new word tokens from an incremental audio segment to the existing subtitle.
    ///     Each segment carries only its new word(s) with slice-relative timing.
    /// </summary>
    public void UpdateSegment(
        SubtitleSegment existingSegment,
        AudioSegment audioSegment,
        float segmentAbsoluteStartTime
    )
    {
        if (audioSegment.Tokens.Count == 0)
        {
            return;
        }

        // Compute the absolute start time for this slice by looking at the
        // last existing word's end time, falling back to the segment start.
        var existingEndTime = segmentAbsoluteStartTime;
        foreach (var line in existingSegment.Lines)
        {
            foreach (var w in line.Words)
            {
                var wordEnd = w.AbsoluteStartTime + w.Duration;
                if (wordEnd > existingEndTime)
                {
                    existingEndTime = wordEnd;
                }
            }
        }

        var cumulativeTimeOffset = 0f;

        for (var i = 0; i < audioSegment.Tokens.Count; i++)
        {
            var token = audioSegment.Tokens[i];
            var (wordStartTimeOffset, wordDuration) = CalculateWordTiming(
                audioSegment,
                i,
                cumulativeTimeOffset
            );

            cumulativeTimeOffset = wordStartTimeOffset + wordDuration;

            var absoluteStart = existingEndTime + wordStartTimeOffset;

            var wordText = token.Text + token.Whitespace;
            var wordSize = textMeasurer.MeasureText(wordText);

            // Create first line if segment was initially empty
            if (existingSegment.Lines.Count == 0)
            {
                existingSegment.AddLine(new SubtitleLine(0, 0));
            }

            var lastLine = existingSegment.Lines[^1];
            var currentLineWidth = 0f;
            foreach (var w in lastLine.Words)
            {
                currentLineWidth += w.Size.X;
            }

            if (currentLineWidth > 0 && currentLineWidth + wordSize.X > textMeasurer.AvailableWidth)
            {
                lastLine = new SubtitleLine(existingSegment.Lines.Count, 0);
                existingSegment.AddLine(lastLine);
            }

            var wordInfo = new SubtitleWordInfo
            {
                Text = wordText,
                Size = wordSize,
                AbsoluteStartTime = absoluteStart,
                Duration = wordDuration,
            };

            lastLine.AddWord(wordInfo);
        }

        // Update estimated end time
        foreach (var line in existingSegment.Lines)
        {
            foreach (var w in line.Words)
            {
                var wordEnd = w.AbsoluteStartTime + w.Duration;
                if (wordEnd > existingSegment.EstimatedEndTime)
                {
                    existingSegment.EstimatedEndTime = wordEnd;
                }
            }
        }
    }

    private (float startOffset, float duration) CalculateWordTiming(
        AudioSegment segment,
        int tokenIndex,
        float cumulativeTimeOffset
    )
    {
        var token = segment.Tokens[tokenIndex];
        float wordStartTimeOffset;
        float wordDuration;

        if (token.StartTs.HasValue)
        {
            wordStartTimeOffset = (float)token.StartTs.Value;
            wordDuration = token.EndTs.HasValue
                ? (float)(token.EndTs.Value - token.StartTs.Value)
                : EstimateDuration(segment, tokenIndex, wordStartTimeOffset);
        }
        else
        {
            wordStartTimeOffset = cumulativeTimeOffset;
            wordDuration = EstimateDuration(segment, tokenIndex, wordStartTimeOffset);
        }

        return (wordStartTimeOffset, Math.Max(0.01f, wordDuration));
    }

    private float EstimateDuration(AudioSegment segment, int tokenIndex, float wordStartOffset)
    {
        if (
            TryFindNextTokenStartOffset(segment, tokenIndex, out var nextStart)
            && nextStart > wordStartOffset
        )
        {
            return nextStart - wordStartOffset;
        }

        return _defaultWordDuration;
    }

    private static bool TryFindNextTokenStartOffset(
        AudioSegment segment,
        int currentTokenIndex,
        out float nextStartOffset
    )
    {
        for (var j = currentTokenIndex + 1; j < segment.Tokens.Count; j++)
        {
            if (segment.Tokens[j].StartTs.HasValue)
            {
                nextStartOffset = (float)segment.Tokens[j].StartTs.Value;
                return true;
            }
        }

        nextStartOffset = 0f;
        return false;
    }
}
