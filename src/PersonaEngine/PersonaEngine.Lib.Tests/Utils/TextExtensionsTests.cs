using PersonaEngine.Lib.Utils.Text;
using Xunit;

namespace PersonaEngine.Lib.Tests.Utils;

public class TextExtensionsTests
{
    [Fact]
    public void TrimPunctuation_NoPunctuation_ReturnsUnchanged()
    {
        Assert.Equal("hello", "hello".TrimPunctuation());
    }

    [Fact]
    public void TrimPunctuation_LeadingPunctuation_Strips()
    {
        Assert.Equal("hello", "...hello".TrimPunctuation());
    }

    [Fact]
    public void TrimPunctuation_TrailingPunctuation_Strips()
    {
        Assert.Equal("hello", "hello!!!".TrimPunctuation());
    }

    [Fact]
    public void TrimPunctuation_BothEnds_Strips()
    {
        Assert.Equal("hello", "\"hello!\"".TrimPunctuation());
    }

    [Fact]
    public void TrimPunctuation_InternalPunctuation_Preserved()
    {
        Assert.Equal("it's", "it's".TrimPunctuation());
    }

    [Fact]
    public void TrimPunctuation_AllPunctuation_ReturnsEmpty()
    {
        Assert.Equal("", "...!!!".TrimPunctuation());
    }

    [Fact]
    public void TrimPunctuation_Empty_ReturnsEmpty()
    {
        Assert.Equal("", "".TrimPunctuation());
    }
}
