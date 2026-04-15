using PersonaEngine.Lib.UI.Overlay;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.Overlay;

/// <summary>
///     Verifies the cursor-to-handle classification for the overlay's two buttons
///     (drag at top-right, resize at bottom-right) so a click lands on the right one
///     and anywhere else falls through to click-through territory.
/// </summary>
public class OverlayChromeRendererHitTestTests
{
    private const int Width = 400;
    private const int Height = 600;

    private const int P = OverlayChromeRenderer.ButtonEdgePadding;
    private const int S = OverlayChromeRenderer.ButtonSize;

    [Fact]
    public void HitTest_OnDragButton_ReturnsDrag()
    {
        // Center of the drag button.
        var x = Width - P - S / 2;
        var y = P + S / 2;

        Assert.Equal(OverlayHandle.Drag, OverlayChromeRenderer.HitTest(x, y, Width, Height));
    }

    [Fact]
    public void HitTest_OnResizeButton_ReturnsResize()
    {
        var x = Width - P - S / 2;
        var y = Height - P - S / 2;

        Assert.Equal(OverlayHandle.Resize, OverlayChromeRenderer.HitTest(x, y, Width, Height));
    }

    [Theory]
    [InlineData(10, 10)] // top-left: no button
    [InlineData(200, 300)] // dead center
    [InlineData(10, 590)] // bottom-left: no button
    public void HitTest_OutsideButtons_ReturnsNone(int x, int y)
    {
        Assert.Equal(OverlayHandle.None, OverlayChromeRenderer.HitTest(x, y, Width, Height));
    }

    [Fact]
    public void HitTest_OutsideRect_ReturnsNone()
    {
        Assert.Equal(OverlayHandle.None, OverlayChromeRenderer.HitTest(-5, -5, Width, Height));
        Assert.Equal(
            OverlayHandle.None,
            OverlayChromeRenderer.HitTest(Width + 10, Height + 10, Width, Height)
        );
    }

    [Fact]
    public void HitTest_JustOutsideDragButton_ReturnsNone()
    {
        // 1 pixel to the left of the drag button's left edge.
        var x = Width - P - S - 1;
        var y = P + S / 2;

        Assert.Equal(OverlayHandle.None, OverlayChromeRenderer.HitTest(x, y, Width, Height));
    }
}
