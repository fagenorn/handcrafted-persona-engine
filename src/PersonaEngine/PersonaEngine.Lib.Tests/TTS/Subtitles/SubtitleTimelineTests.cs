using System.Numerics;
using FontStashSharp;
using PersonaEngine.Lib.UI.Rendering.Subtitles;
using Xunit;

namespace PersonaEngine.Lib.Tests.TTS.Subtitles;

public class SubtitleTimelineTests
{
    private readonly SubtitleTimeline _timeline;
    private readonly FSColor _highlight = new(79, 195, 247, 255);
    private readonly FSColor _normal = new(255, 255, 255, 255);

    public SubtitleTimelineTests()
    {
        _timeline = new SubtitleTimeline(
            maxVisibleLines: 3,
            bottomMargin: 50f,
            lineSpacing: 40f,
            interSegmentSpacing: 16f,
            wordAnimator: new PopAnimator(),
            highlightColor: _highlight,
            normalColor: _normal
        );
    }

    [Fact]
    public void AddSegment_IncreasesCount()
    {
        var segment = CreateSegment(startTime: 0f, wordCount: 2);

        _timeline.AddSegment(segment);

        Assert.Equal(1, _timeline.ActiveSegmentCount);
    }

    [Fact]
    public void RemoveSegmentById_RemovesCorrectSegment()
    {
        var seg1 = CreateSegment(startTime: 0f, wordCount: 1);
        var seg2 = CreateSegment(startTime: 1f, wordCount: 1);
        _timeline.AddSegment(seg1);
        _timeline.AddSegment(seg2);

        _timeline.RemoveSegment(seg1.Id);

        Assert.Equal(1, _timeline.ActiveSegmentCount);
    }

    [Fact]
    public void ExpireOldSegments_RemovesExpiredOnly()
    {
        var old = CreateSegment(startTime: 0f, wordCount: 1, wordDuration: 0.5f);
        var current = CreateSegment(startTime: 5f, wordCount: 1, wordDuration: 0.5f);
        _timeline.AddSegment(old);
        _timeline.AddSegment(current);

        _timeline.ExpireOldSegments(currentTime: 3.0f, bufferSeconds: 1.0f);

        Assert.Equal(1, _timeline.ActiveSegmentCount);
    }

    [Fact]
    public void Update_SetsAnimationProgress_ForActiveWords()
    {
        var segment = CreateSegment(startTime: 0f, wordCount: 1, wordDuration: 1.0f);
        _timeline.AddSegment(segment);

        _timeline.Update(0.5f);

        var word = segment.Lines[0].Words[0];
        Assert.Equal(0.5f, word.AnimationProgress, 0.05f);
    }

    [Fact]
    public void Update_SetsProgressToOne_ForCompletedWords()
    {
        var segment = CreateSegment(startTime: 0f, wordCount: 1, wordDuration: 0.5f);
        _timeline.AddSegment(segment);

        _timeline.Update(1.0f);

        var word = segment.Lines[0].Words[0];
        Assert.Equal(1.0f, word.AnimationProgress);
    }

    [Fact]
    public void Update_SetsProgressToZero_ForFutureWords()
    {
        var segment = CreateSegment(startTime: 5f, wordCount: 1, wordDuration: 0.5f);
        _timeline.AddSegment(segment);

        _timeline.Update(0f);

        var word = segment.Lines[0].Words[0];
        Assert.Equal(0f, word.AnimationProgress);
    }

    [Fact]
    public void GetVisibleLines_RespectsMaxLines()
    {
        var segment = CreateMultiLineSegment(startTime: 0f, lineCount: 5, wordDuration: 10f);
        _timeline.AddSegment(segment);

        var output = new List<SubtitleLine>();
        _timeline.GetVisibleLinesAndPosition(50f, 1920, 1080, output);

        Assert.Equal(3, output.Count); // maxVisibleLines = 3
    }

    [Fact]
    public void GetVisibleLines_SkipsFutureLines()
    {
        var segment = CreateSegment(startTime: 10f, wordCount: 1, wordDuration: 0.5f);
        _timeline.AddSegment(segment);

        var output = new List<SubtitleLine>();
        _timeline.GetVisibleLinesAndPosition(0f, 1920, 1080, output);

        Assert.Empty(output);
    }

    [Fact]
    public void ClearAll_RemovesAllSegments()
    {
        _timeline.AddSegment(CreateSegment(0f, 1));
        _timeline.AddSegment(CreateSegment(1f, 1));

        _timeline.ClearAll();

        Assert.Equal(0, _timeline.ActiveSegmentCount);
    }

    [Fact]
    public void RemoveSegment_NonexistentId_DoesNothing()
    {
        _timeline.AddSegment(CreateSegment(0f, 1));

        _timeline.RemoveSegment(Guid.NewGuid());

        Assert.Equal(1, _timeline.ActiveSegmentCount);
    }

    private static SubtitleSegment CreateSegment(
        float startTime,
        int wordCount,
        float wordDuration = 0.5f
    )
    {
        var segment = new SubtitleSegment(startTime, "test");
        var line = new SubtitleLine(0, 0);
        for (var i = 0; i < wordCount; i++)
        {
            line.AddWord(
                new SubtitleWordInfo
                {
                    Text = $"word{i} ",
                    Size = new Vector2(50f, 20f),
                    AbsoluteStartTime = startTime + i * wordDuration,
                    Duration = wordDuration,
                }
            );
        }
        segment.AddLine(line);
        return segment;
    }

    private static SubtitleSegment CreateMultiLineSegment(
        float startTime,
        int lineCount,
        float wordDuration = 0.5f
    )
    {
        var segment = new SubtitleSegment(startTime, "test");
        for (var l = 0; l < lineCount; l++)
        {
            var line = new SubtitleLine(0, l);
            line.AddWord(
                new SubtitleWordInfo
                {
                    Text = $"line{l} ",
                    Size = new Vector2(50f, 20f),
                    AbsoluteStartTime = startTime + l * wordDuration,
                    Duration = wordDuration,
                }
            );
            segment.AddLine(line);
        }
        return segment;
    }
}
