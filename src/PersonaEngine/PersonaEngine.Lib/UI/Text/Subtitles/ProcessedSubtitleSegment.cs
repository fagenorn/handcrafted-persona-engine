using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.UI.Text.Subtitles;

public record ProcessedSubtitleSegment
{
    public ProcessedSubtitleSegment(AudioSegment originalSegment, string fullText, float segmentStartTime)
    {
        OriginalSegment  = originalSegment;
        FullText         = fullText;
        SegmentStartTime = segmentStartTime;
    }

    public AudioSegment OriginalSegment { get; }

    public string FullText { get; }

    public float SegmentStartTime { get; }

    public List<ProcessedSubtitleLine> Lines { get; } = new();
}