using PersonaEngine.Lib.UI.ControlPanel;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.ControlPanel;

public class TimeFormatTests
{
    [Theory]
    [InlineData(0, "just now")]
    [InlineData(0.5, "just now")]
    [InlineData(0.999, "just now")]
    public void HumaniseAgo_UnderOneSecond_ReturnsJustNow(double seconds, string expected)
    {
        Assert.Equal(expected, TimeFormat.HumaniseAgo(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(-1, "just now")]
    [InlineData(-3600, "just now")]
    public void HumaniseAgo_NegativeSpan_ReturnsJustNow(double seconds, string expected)
    {
        Assert.Equal(expected, TimeFormat.HumaniseAgo(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(1, "1s ago")]
    [InlineData(5, "5s ago")]
    [InlineData(59, "59s ago")]
    public void HumaniseAgo_Seconds(double seconds, string expected)
    {
        Assert.Equal(expected, TimeFormat.HumaniseAgo(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(1, "1m ago")]
    [InlineData(15, "15m ago")]
    [InlineData(59, "59m ago")]
    public void HumaniseAgo_Minutes(double minutes, string expected)
    {
        Assert.Equal(expected, TimeFormat.HumaniseAgo(TimeSpan.FromMinutes(minutes)));
    }

    [Theory]
    [InlineData(1, "1h ago")]
    [InlineData(3, "3h ago")]
    [InlineData(23, "23h ago")]
    public void HumaniseAgo_Hours(double hours, string expected)
    {
        Assert.Equal(expected, TimeFormat.HumaniseAgo(TimeSpan.FromHours(hours)));
    }

    [Theory]
    [InlineData(1, "1d ago")]
    [InlineData(6, "6d ago")]
    public void HumaniseAgo_Days_Cached(double days, string expected)
    {
        Assert.Equal(expected, TimeFormat.HumaniseAgo(TimeSpan.FromDays(days)));
    }

    [Theory]
    [InlineData(7, "7d ago")]
    [InlineData(42, "42d ago")]
    [InlineData(365, "365d ago")]
    public void HumaniseAgo_Days_Uncached(double days, string expected)
    {
        Assert.Equal(expected, TimeFormat.HumaniseAgo(TimeSpan.FromDays(days)));
    }

    [Fact]
    public void HumaniseAgo_CachedBuckets_ReturnReferenceIdenticalStrings()
    {
        // Validates the zero-allocation contract: repeated calls in the cached
        // range return the exact same interned string so ProbeFooter can use a
        // reference-equality check to skip its interpolation.
        var a = TimeFormat.HumaniseAgo(TimeSpan.FromSeconds(5));
        var b = TimeFormat.HumaniseAgo(TimeSpan.FromSeconds(5.9));
        Assert.Same(a, b);

        var c = TimeFormat.HumaniseAgo(TimeSpan.FromMinutes(15));
        var d = TimeFormat.HumaniseAgo(TimeSpan.FromMinutes(15.5));
        Assert.Same(c, d);

        var e = TimeFormat.HumaniseAgo(TimeSpan.FromHours(3));
        var f = TimeFormat.HumaniseAgo(TimeSpan.FromHours(3.99));
        Assert.Same(e, f);
    }
}
