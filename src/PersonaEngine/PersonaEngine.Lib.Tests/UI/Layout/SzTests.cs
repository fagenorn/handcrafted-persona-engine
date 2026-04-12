using PersonaEngine.Lib.UI.ControlPanel.Layout;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.Layout;

public class SzTests
{
    [Fact]
    public void Fixed_StoresValueAndIsFixed()
    {
        var sz = Sz.Fixed(30f);
        Assert.True(sz.IsFixed);
        Assert.Equal(30f, sz.Value);
    }

    [Fact]
    public void Fill_StoresWeightAndIsNotFixed()
    {
        var sz = Sz.Fill(2f);
        Assert.False(sz.IsFixed);
        Assert.Equal(2f, sz.Value);
    }

    [Fact]
    public void Fill_DefaultWeightIsOne()
    {
        var sz = Sz.Fill();
        Assert.Equal(1f, sz.Value);
    }

    [Fact]
    public void Resolve_SingleFixed()
    {
        var sizes = new[] { Sz.Fixed(100f) };
        var results = Sz.Resolve(sizes, 500f);
        Assert.Equal(100f, results[0]);
    }

    [Fact]
    public void Resolve_SingleFill_TakesAllSpace()
    {
        var sizes = new[] { Sz.Fill() };
        var results = Sz.Resolve(sizes, 500f);
        Assert.Equal(500f, results[0]);
    }

    [Fact]
    public void Resolve_FixedAndFill()
    {
        var sizes = new[] { Sz.Fixed(30f), Sz.Fill(), Sz.Fixed(40f) };
        var results = Sz.Resolve(sizes, 800f);
        Assert.Equal(30f, results[0]);
        Assert.Equal(730f, results[1]);
        Assert.Equal(40f, results[2]);
    }

    [Fact]
    public void Resolve_MultipleFills_DistributeByWeight()
    {
        var sizes = new[] { Sz.Fill(2f), Sz.Fill(1f) };
        var results = Sz.Resolve(sizes, 900f);
        Assert.Equal(600f, results[0]);
        Assert.Equal(300f, results[1]);
    }

    [Fact]
    public void Resolve_ClampsToZero_WhenFixedExceedsAvailable()
    {
        var sizes = new[] { Sz.Fixed(600f), Sz.Fill(), Sz.Fixed(400f) };
        var results = Sz.Resolve(sizes, 800f);
        Assert.Equal(600f, results[0]);
        Assert.Equal(0f, results[1]);
        Assert.Equal(400f, results[2]);
    }
}
