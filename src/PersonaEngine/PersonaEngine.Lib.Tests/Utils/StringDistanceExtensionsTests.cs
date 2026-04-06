using PersonaEngine.Lib.Utils;
using Xunit;

namespace PersonaEngine.Lib.Tests.Utils;

public class StringDistanceExtensionsTests
{
    // --- LevenshteinDistance ---

    [Fact]
    public void LevenshteinDistance_IdenticalStrings_ReturnsZero()
    {
        Assert.Equal(0, StringDistanceExtensions.LevenshteinDistance("HELLO", "HELLO"));
    }

    [Fact]
    public void LevenshteinDistance_EmptyToNonEmpty_ReturnsLength()
    {
        Assert.Equal(5, StringDistanceExtensions.LevenshteinDistance("", "HELLO"));
    }

    [Fact]
    public void LevenshteinDistance_NonEmptyToEmpty_ReturnsLength()
    {
        Assert.Equal(3, StringDistanceExtensions.LevenshteinDistance("CAT", ""));
    }

    [Fact]
    public void LevenshteinDistance_BothEmpty_ReturnsZero()
    {
        Assert.Equal(0, StringDistanceExtensions.LevenshteinDistance("", ""));
    }

    [Fact]
    public void LevenshteinDistance_SingleSubstitution()
    {
        Assert.Equal(1, StringDistanceExtensions.LevenshteinDistance("CAT", "BAT"));
    }

    [Fact]
    public void LevenshteinDistance_Insertion()
    {
        Assert.Equal(1, StringDistanceExtensions.LevenshteinDistance("CAT", "CATS"));
    }

    [Fact]
    public void LevenshteinDistance_Deletion()
    {
        Assert.Equal(1, StringDistanceExtensions.LevenshteinDistance("CATS", "CAT"));
    }

    [Fact]
    public void LevenshteinDistance_MultipleEdits()
    {
        Assert.Equal(3, StringDistanceExtensions.LevenshteinDistance("KITTEN", "SITTING"));
    }

    // --- NormalizedSimilarity ---

    [Fact]
    public void NormalizedSimilarity_Identical_ReturnsOne()
    {
        Assert.Equal(1f, StringDistanceExtensions.NormalizedSimilarity("HELLO", "HELLO"));
    }

    [Fact]
    public void NormalizedSimilarity_CompletelyDifferent_ReturnsLowValue()
    {
        var sim = StringDistanceExtensions.NormalizedSimilarity("ABC", "XYZ");
        Assert.True(sim < 0.1f);
    }

    [Fact]
    public void NormalizedSimilarity_BothEmpty_ReturnsOne()
    {
        Assert.Equal(1f, StringDistanceExtensions.NormalizedSimilarity("", ""));
    }

    [Fact]
    public void NormalizedSimilarity_OneEdit_HighSimilarity()
    {
        Assert.Equal(0.8f, StringDistanceExtensions.NormalizedSimilarity("HELLO", "HXLLO"));
    }

    // --- KeepOnlyLetters ---

    [Fact]
    public void KeepOnlyLetters_NoPunctuation_ReturnsUnchanged()
    {
        Assert.Equal("HELLO", "HELLO".KeepOnlyLetters());
    }

    [Fact]
    public void KeepOnlyLetters_MixedPunctuation_StripsAll()
    {
        Assert.Equal("HELLO", "H.E,L!L?O".KeepOnlyLetters());
    }

    [Fact]
    public void KeepOnlyLetters_OnlyPunctuation_ReturnsEmpty()
    {
        Assert.Equal("", "...!!!".KeepOnlyLetters());
    }

    [Fact]
    public void KeepOnlyLetters_WithDigits_StripsDigits()
    {
        Assert.Equal("ABC", "A1B2C3".KeepOnlyLetters());
    }

    [Fact]
    public void KeepOnlyLetters_Empty_ReturnsEmpty()
    {
        Assert.Equal("", "".KeepOnlyLetters());
    }
}
