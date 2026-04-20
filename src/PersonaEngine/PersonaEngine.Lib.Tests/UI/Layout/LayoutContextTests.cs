using PersonaEngine.Lib.UI.ControlPanel.Layout;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.Layout;

public class LayoutContextTests
{
    [Fact]
    public void PushPeekPop_RoundTrips()
    {
        LayoutContext.Push(800f, 600f);
        var ctx = LayoutContext.Peek();
        Assert.Equal(800f, ctx.Width);
        Assert.Equal(600f, ctx.Height);
        LayoutContext.Pop();
    }

    [Fact]
    public void NestedPush_InnerOverridesOuter()
    {
        LayoutContext.Push(800f, 600f);
        LayoutContext.Push(400f, 300f);
        var inner = LayoutContext.Peek();
        Assert.Equal(400f, inner.Width);
        Assert.Equal(300f, inner.Height);
        LayoutContext.Pop();
        var outer = LayoutContext.Peek();
        Assert.Equal(800f, outer.Width);
        LayoutContext.Pop();
    }

    [Fact]
    public void ConsumeHeight_ReducesRemaining()
    {
        LayoutContext.Push(800f, 600f);
        LayoutContext.ConsumeHeight(30f);
        Assert.Equal(570f, LayoutContext.RemainingHeight());
        LayoutContext.ConsumeHeight(40f);
        Assert.Equal(530f, LayoutContext.RemainingHeight());
        LayoutContext.Pop();
    }

    [Fact]
    public void ConsumeHeight_ClampsToZero()
    {
        LayoutContext.Push(800f, 100f);
        LayoutContext.ConsumeHeight(150f);
        Assert.Equal(0f, LayoutContext.RemainingHeight());
        LayoutContext.Pop();
    }
}
