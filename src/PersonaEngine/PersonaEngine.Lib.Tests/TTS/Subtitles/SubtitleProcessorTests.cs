using System.Numerics;
using FontStashSharp;
using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.UI.Text.Subtitles;
using Xunit;

namespace PersonaEngine.Lib.Tests.TTS.Subtitles;

public class SubtitleProcessorTests
{
    private readonly SubtitleProcessor _processor;

    public SubtitleProcessorTests()
    {
        var textMeasurer = CreateTestMeasurer();
        _processor = new SubtitleProcessor(textMeasurer, defaultWordDuration: 0.3f);
    }

    [Fact]
    public void ProcessSegment_NullAudioSegment_ReturnsEmptySegment()
    {
        var result = _processor.ProcessSegment(null, 0f);

        Assert.NotNull(result);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void ProcessSegment_EmptyTokens_ReturnsEmptySegment()
    {
        var segment = new AudioSegment(Memory<float>.Empty, 24000, new List<Token>());

        var result = _processor.ProcessSegment(segment, 0f);

        Assert.NotNull(result);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void ProcessSegment_SingleTokenWithTiming_CreatesOneWord()
    {
        var tokens = new List<Token>
        {
            new()
            {
                Text = "Hello",
                Whitespace = " ",
                StartTs = 0.0,
                EndTs = 0.5,
            },
        };
        var segment = new AudioSegment(new float[12000], 24000, tokens);

        var result = _processor.ProcessSegment(segment, 10.0f);

        Assert.Single(result.Lines);
        Assert.Single(result.Lines[0].Words);
        var word = result.Lines[0].Words[0];
        Assert.Equal("Hello ", word.Text);
        Assert.Equal(10.0f, word.AbsoluteStartTime, 0.001f);
        Assert.Equal(0.5f, word.Duration, 0.001f);
    }

    [Fact]
    public void ProcessSegment_TokenWithoutTiming_UsesDefaultDuration()
    {
        var tokens = new List<Token>
        {
            new()
            {
                Text = "Hello",
                Whitespace = " ",
                StartTs = null,
                EndTs = null,
            },
        };
        var segment = new AudioSegment(new float[12000], 24000, tokens);

        var result = _processor.ProcessSegment(segment, 0f);

        var word = result.Lines[0].Words[0];
        Assert.Equal(0.3f, word.Duration, 0.001f);
    }

    [Fact]
    public void ProcessSegment_AbsoluteStartTime_OffsetsAllWords()
    {
        var tokens = new List<Token>
        {
            new()
            {
                Text = "A",
                Whitespace = " ",
                StartTs = 0.1,
                EndTs = 0.3,
            },
            new()
            {
                Text = "B",
                Whitespace = "",
                StartTs = 0.3,
                EndTs = 0.5,
            },
        };
        var segment = new AudioSegment(new float[12000], 24000, tokens);

        var result = _processor.ProcessSegment(segment, 5.0f);

        Assert.Equal(5.1f, result.Lines[0].Words[0].AbsoluteStartTime, 0.001f);
        Assert.Equal(5.3f, result.Lines[0].Words[1].AbsoluteStartTime, 0.001f);
    }

    [Fact]
    public void ProcessSegment_EstimatedEndTime_IsLastWordEnd()
    {
        var tokens = new List<Token>
        {
            new()
            {
                Text = "A",
                Whitespace = " ",
                StartTs = 0.0,
                EndTs = 0.3,
            },
            new()
            {
                Text = "B",
                Whitespace = "",
                StartTs = 0.3,
                EndTs = 0.8,
            },
        };
        var segment = new AudioSegment(new float[24000], 24000, tokens);

        var result = _processor.ProcessSegment(segment, 2.0f);

        Assert.Equal(2.8f, result.EstimatedEndTime, 0.001f);
    }

    [Fact]
    public void ProcessSegment_ZeroDurationToken_GetsMinimumDuration()
    {
        var tokens = new List<Token>
        {
            new()
            {
                Text = "A",
                Whitespace = "",
                StartTs = 0.5,
                EndTs = 0.5,
            },
        };
        var segment = new AudioSegment(new float[12000], 24000, tokens);

        var result = _processor.ProcessSegment(segment, 0f);

        Assert.True(result.Lines[0].Words[0].Duration >= 0.01f);
    }

    [Fact]
    public void ProcessSegment_TokenWithOnlyStartTs_EstimatesDurationFromNextToken()
    {
        var tokens = new List<Token>
        {
            new()
            {
                Text = "A",
                Whitespace = " ",
                StartTs = 0.0,
                EndTs = null,
            },
            new()
            {
                Text = "B",
                Whitespace = "",
                StartTs = 0.4,
                EndTs = 0.6,
            },
        };
        var segment = new AudioSegment(new float[12000], 24000, tokens);

        var result = _processor.ProcessSegment(segment, 0f);

        // Duration should be estimated as next token start (0.4) - this token start (0.0) = 0.4
        Assert.Equal(0.4f, result.Lines[0].Words[0].Duration, 0.001f);
    }

    private static TextMeasurer CreateTestMeasurer()
    {
        var fontSystem = new FontSystem();
        var fontPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
            "arial.ttf"
        );
        if (File.Exists(fontPath))
        {
            fontSystem.AddFont(File.ReadAllBytes(fontPath));
        }

        var font = fontSystem.GetFont(24);
        return new TextMeasurer(font, sideMargin: 20f, initialWidth: 1920, initialHeight: 1080);
    }
}
