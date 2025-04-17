using System.Text;

using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.UI.Text.Subtitles;

/// <summary>
/// Processes raw AudioSegments into structured SubtitleSegments containing lines and words
/// with calculated timing and layout information.
/// </summary>
public class SubtitleProcessor(TextMeasurer textMeasurer, float defaultWordDuration = 0.3f)
{
    private readonly float _defaultWordDuration = Math.Max(0.01f, defaultWordDuration);

    public SubtitleSegment ProcessSegment(AudioSegment audioSegment, float segmentAbsoluteStartTime)
    {
        if ( audioSegment?.Tokens == null || audioSegment.Tokens.Count == 0 )
        {
            return new SubtitleSegment(audioSegment!, segmentAbsoluteStartTime, string.Empty);
        }

        // --- 1. Build Full Text ---

        var textBuilder = new StringBuilder();
        foreach ( var token in audioSegment.Tokens )
        {
            textBuilder.Append(token.Text);
            textBuilder.Append(token.Whitespace);
        }

        var fullText = textBuilder.ToString();

        var processedSegment     = new SubtitleSegment(audioSegment, segmentAbsoluteStartTime, fullText);
        var currentLine          = new SubtitleLine(0, 0);
        var currentLineWidth     = 0f;
        var cumulativeTimeOffset = 0f;

        // --- 2. Iterate Tokens, Calculate Timing & Layout ---
        for ( var i = 0; i < audioSegment.Tokens.Count; i++ )
        {
            var token    = audioSegment.Tokens[i];
            var wordText = token.Text + token.Whitespace;
            var wordSize = textMeasurer.MeasureText(wordText);

            // --- 3. Line Breaking ---
            if ( currentLineWidth > 0 && currentLineWidth + wordSize.X > textMeasurer.AvailableWidth )
            {
                processedSegment.AddLine(currentLine);
                currentLine      = new SubtitleLine(processedSegment.Lines.Count, 0); // TODO: Get segment index properly if needed
                currentLineWidth = 0f;
            }

            // --- 4. Word Timing Calculation ---
            float wordStartTimeOffset;
            float wordDuration;

            if ( token.StartTs.HasValue )
            {
                wordStartTimeOffset = (float)token.StartTs.Value;
                if ( token.EndTs.HasValue )
                {
                    // Case 1: Start and End provided
                    wordDuration = (float)(token.EndTs.Value - token.StartTs.Value);
                }
                else
                {
                    // Case 2: Only Start provided - Estimate duration until next token or use default
                    var nextTokenStartOffset = FindNextTokenStartOffset(audioSegment, i);
                    if ( nextTokenStartOffset > wordStartTimeOffset )
                    {
                        wordDuration = nextTokenStartOffset - wordStartTimeOffset;
                    }
                    else
                    {
                        wordDuration = _defaultWordDuration;
                    }
                }
            }
            else
            {
                // Case 3: No Start provided - Estimate start based on previous word's end or cumulative time
                wordStartTimeOffset = cumulativeTimeOffset;
                var nextTokenStartOffset = FindNextTokenStartOffset(audioSegment, i);
                if ( nextTokenStartOffset > wordStartTimeOffset )
                {
                    wordDuration = nextTokenStartOffset - wordStartTimeOffset;
                }
                else
                {
                    wordDuration = _defaultWordDuration;
                }
            }

            wordDuration = Math.Max(0.01f, wordDuration);

            cumulativeTimeOffset = wordStartTimeOffset + wordDuration;

            // --- 5. Create Word Info ---
            var wordInfo = new SubtitleWordInfo { Text = wordText, Size = wordSize, AbsoluteStartTime = segmentAbsoluteStartTime + wordStartTimeOffset, Duration = wordDuration };

            currentLine.AddWord(wordInfo);
            currentLineWidth += wordSize.X;
        }

        if ( currentLine.Words.Count > 0 )
        {
            processedSegment.AddLine(currentLine);
        }

        return processedSegment;
    }

    private float FindNextTokenStartOffset(AudioSegment segment, int currentTokenIndex)
    {
        for ( var j = currentTokenIndex + 1; j < segment.Tokens.Count; j++ )
        {
            if ( segment.Tokens[j].StartTs.HasValue )
            {
                return (float)segment.Tokens[j].StartTs!.Value;
            }
        }

        // Indicate not found or end of segment
        // Return a value that ensures the calling logic uses the default duration.
        // Returning -1 or float.MaxValue could work, depending on how it's used.
        // Let's return a value <= the current offset to trigger default duration.
        return segment.Tokens[currentTokenIndex].StartTs.HasValue ? (float)segment.Tokens[currentTokenIndex].StartTs!.Value : 0f;
    }
}