using PersonaEngine.Lib.UI.ControlPanel;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI;

public class EasingTests
{
    [Fact]
    public void EaseOutBack_AtZero_ReturnsZero()
    {
        Assert.Equal(0f, Easing.EaseOutBack(0f), precision: 4);
    }

    [Fact]
    public void EaseOutBack_AtOne_ReturnsOne()
    {
        Assert.Equal(1f, Easing.EaseOutBack(1f), precision: 4);
    }

    [Fact]
    public void EaseOutBack_Overshoots_PastOne()
    {
        var midValue = Easing.EaseOutBack(0.5f);
        Assert.True(midValue > 1f, $"Expected overshoot past 1.0, got {midValue}");
    }
}
