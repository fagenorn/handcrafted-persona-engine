using PersonaEngine.Lib.UI.Overlay;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.Overlay;

/// <summary>
///     Verifies the cursor-to-handle classification for the overlay's two buttons.
///     The drag button sits in the top-right corner; the resize button sits in
///     the bottom-right corner and drives an SE-corner resize (top-left pinned).
/// </summary>
public class OverlayChromeLayoutTests
{
    private const int Width = 400;
    private const int Height = 600;

    private const int P = OverlayChromeLayout.ButtonEdgePadding;
    private const int S = OverlayChromeLayout.ButtonSize;

    [Fact]
    public void HitTest_OnDragButton_ReturnsDrag()
    {
        // Center of the drag button (top-right corner).
        var x = Width - P - S / 2;
        var y = P + S / 2;

        Assert.Equal(OverlayHandle.Drag, OverlayChromeLayout.HitTest(x, y, Width, Height));
    }

    [Fact]
    public void HitTest_OnResizeButton_ReturnsResize()
    {
        // Center of the resize button (bottom-right corner).
        var x = Width - P - S / 2;
        var y = Height - P - S / 2;

        Assert.Equal(OverlayHandle.Resize, OverlayChromeLayout.HitTest(x, y, Width, Height));
    }

    [Theory]
    [InlineData(10, 10)] // top-left: no button
    [InlineData(200, 300)] // dead center
    [InlineData(10, 590)] // bottom-left: no button
    [InlineData(Width - P - S - 4 - S / 2, P + S / 2)] // slot left of drag: used to be resize, now empty
    public void HitTest_OutsideButtons_ReturnsNone(int x, int y)
    {
        Assert.Equal(OverlayHandle.None, OverlayChromeLayout.HitTest(x, y, Width, Height));
    }

    [Fact]
    public void HitTest_OutsideRect_ReturnsNone()
    {
        Assert.Equal(OverlayHandle.None, OverlayChromeLayout.HitTest(-5, -5, Width, Height));
        Assert.Equal(
            OverlayHandle.None,
            OverlayChromeLayout.HitTest(Width + 10, Height + 10, Width, Height)
        );
    }

    [Fact]
    public void HitTest_JustOutsideDragButton_ReturnsNone()
    {
        // 1 pixel to the left of the drag button's left edge.
        var x = Width - P - S - 1;
        var y = P + S / 2;

        Assert.Equal(OverlayHandle.None, OverlayChromeLayout.HitTest(x, y, Width, Height));
    }

    [Fact]
    public void HitTest_JustOutsideResizeButton_ReturnsNone()
    {
        // 1 pixel above the resize button's top edge.
        var x = Width - P - S / 2;
        var y = Height - P - S - 1;

        Assert.Equal(OverlayHandle.None, OverlayChromeLayout.HitTest(x, y, Width, Height));
    }
}
