using PersonaEngine.Lib.UI.ControlPanel;
using PersonaEngine.Lib.UI.Overlay;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.ControlPanel;

public class StatusPillTests
{
    // StatusPill.Render draws via ImGui — not practical to unit-test visually.
    // We cover the label-override contract through the helper method it wraps,
    // which we expose internally for testability.
    [Fact]
    public void ResolveLabel_WithOverride_ReturnsOverride()
    {
        var result = StatusPill.ResolveLabel(OverlayStatus.Active, overrideLabel: "Ready");
        Assert.Equal("Ready", result);
    }

    [Fact]
    public void ResolveLabel_NoOverride_FallsBackToStatusVisuals()
    {
        var result = StatusPill.ResolveLabel(OverlayStatus.Active, overrideLabel: null);
        Assert.Equal("Active", result);
    }

    [Fact]
    public void ResolveLabel_EmptyOverride_StillUsesOverride()
    {
        // Empty string is a caller-legitimate label (e.g. "dot only" variant).
        var result = StatusPill.ResolveLabel(OverlayStatus.Off, overrideLabel: "");
        Assert.Equal("", result);
    }
}
