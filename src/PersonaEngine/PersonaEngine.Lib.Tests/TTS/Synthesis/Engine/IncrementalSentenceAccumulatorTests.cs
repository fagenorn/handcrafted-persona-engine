using NSubstitute;
using PersonaEngine.Lib.TTS.Synthesis.Engine;
using PersonaEngine.Lib.TTS.Synthesis.TextProcessing;
using Xunit;

namespace PersonaEngine.Lib.Tests.TTS.Synthesis.Engine;

public class IncrementalSentenceAccumulatorTests
{
    private readonly ITextNormalizer _normalizer = Substitute.For<ITextNormalizer>();
    private readonly ISentenceSegmenter _segmenter = Substitute.For<ISentenceSegmenter>();

    private IncrementalSentenceAccumulator CreateAccumulator() =>
        new(_normalizer, _segmenter);

    [Fact]
    public void TakeCompletedSentences_NoPunctuation_ReturnsEmpty()
    {
        var acc = CreateAccumulator();

        acc.Append("Hello world");
        var result = acc.TakeCompletedSentences();

        Assert.Empty(result);
        _normalizer.DidNotReceive().Normalize(Arg.Any<string>());
        _segmenter.DidNotReceive().Segment(Arg.Any<string>());
    }

    [Fact]
    public void TakeCompletedSentences_WithPunctuation_SegmentsAndReturnsCompleted()
    {
        var acc = CreateAccumulator();
        _normalizer.Normalize("Hello world. How are").Returns("Hello world. How are");
        _segmenter.Segment("Hello world. How are").Returns(
            new List<string> { "Hello world.", "How are" }
        );

        acc.Append("Hello world. How are");
        var result = acc.TakeCompletedSentences();

        Assert.Single(result);
        Assert.Equal("Hello world.", result[0]);
    }

    [Fact]
    public void TakeCompletedSentences_RetainsIncompleteLastSentence()
    {
        var acc = CreateAccumulator();
        _normalizer.Normalize("Hello world. How are").Returns("Hello world. How are");
        _segmenter.Segment("Hello world. How are").Returns(
            new List<string> { "Hello world.", "How are" }
        );

        acc.Append("Hello world. How are");
        acc.TakeCompletedSentences();

        _normalizer.Normalize("How are you doing?").Returns("How are you doing?");
        _segmenter.Segment("How are you doing?").Returns(
            new List<string> { "How are you doing?" }
        );

        acc.Append(" you doing?");
        var result = acc.TakeCompletedSentences();

        Assert.Empty(result);
    }

    [Fact]
    public void TakeCompletedSentences_MultipleSentences_ReturnsAllButLast()
    {
        var acc = CreateAccumulator();
        _normalizer.Normalize("A. B. C").Returns("A. B. C");
        _segmenter.Segment("A. B. C").Returns(
            new List<string> { "A.", "B.", "C" }
        );

        acc.Append("A. B. C");
        var result = acc.TakeCompletedSentences();

        Assert.Equal(2, result.Count);
        Assert.Equal("A.", result[0]);
        Assert.Equal("B.", result[1]);
    }

    [Fact]
    public void TakeCompletedSentences_OneSentenceWithPunctuation_ReturnsEmpty()
    {
        var acc = CreateAccumulator();
        _normalizer.Normalize("Hello world.").Returns("Hello world.");
        _segmenter.Segment("Hello world.").Returns(
            new List<string> { "Hello world." }
        );

        acc.Append("Hello world.");
        var result = acc.TakeCompletedSentences();

        Assert.Empty(result);
    }

    [Fact]
    public void Flush_ReturnsRemainingText()
    {
        var acc = CreateAccumulator();
        _normalizer.Normalize("Hello world").Returns("Hello world");
        _segmenter.Segment("Hello world").Returns(
            new List<string> { "Hello world" }
        );

        acc.Append("Hello world");
        var result = acc.Flush();

        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void Flush_EmptyBuffer_ReturnsNull()
    {
        var acc = CreateAccumulator();

        var result = acc.Flush();

        Assert.Null(result);
    }

    [Fact]
    public void Flush_WhitespaceOnly_ReturnsNull()
    {
        var acc = CreateAccumulator();
        _normalizer.Normalize("   ").Returns("   ");
        _segmenter.Segment("   ").Returns(new List<string> { "   " });

        acc.Append("   ");
        var result = acc.Flush();

        Assert.Null(result);
    }

    [Fact]
    public void Reset_ClearsBuffer()
    {
        var acc = CreateAccumulator();
        acc.Append("Hello world");

        acc.Reset();
        var result = acc.Flush();

        Assert.Null(result);
    }

    [Fact]
    public void Append_SemicolonTriggersPunctuation()
    {
        var acc = CreateAccumulator();
        _normalizer.Normalize("first; second").Returns("first; second");
        _segmenter.Segment("first; second").Returns(
            new List<string> { "first;", "second" }
        );

        acc.Append("first; second");
        var result = acc.TakeCompletedSentences();

        Assert.Single(result);
        _normalizer.Received(1).Normalize(Arg.Any<string>());
    }

    [Fact]
    public void Append_QuestionMarkTriggersPunctuation()
    {
        var acc = CreateAccumulator();
        _normalizer.Normalize("Really? Yes").Returns("Really? Yes");
        _segmenter.Segment("Really? Yes").Returns(
            new List<string> { "Really?", "Yes" }
        );

        acc.Append("Really? Yes");
        var result = acc.TakeCompletedSentences();

        Assert.Single(result);
    }

    [Fact]
    public void Append_ExclamationMarkTriggersPunctuation()
    {
        var acc = CreateAccumulator();
        _normalizer.Normalize("Wow! Cool").Returns("Wow! Cool");
        _segmenter.Segment("Wow! Cool").Returns(
            new List<string> { "Wow!", "Cool" }
        );

        acc.Append("Wow! Cool");
        var result = acc.TakeCompletedSentences();

        Assert.Single(result);
    }
}
