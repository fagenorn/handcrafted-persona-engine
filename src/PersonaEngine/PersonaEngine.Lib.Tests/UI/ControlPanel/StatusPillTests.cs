using PersonaEngine.Lib.UI.ControlPanel;
using PersonaEngine.Lib.UI.Overlay;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.ControlPanel;

public class StatusPillTests
{
    // StatusPill.Render draws via ImGui — not practical to unit-test visually.
    // The label-override contract is covered through ResolveLabel, which is
    // exposed internally for testability.
    [Theory]
    [InlineData(OverlayStatus.Active, "Ready", "Ready")] // explicit override wins
    [InlineData(OverlayStatus.Active, null, "Active")] // null falls back to StatusVisuals
    [InlineData(OverlayStatus.Off, "", "")] // empty string is a legitimate "dot only" label
    public void ResolveLabel_ReturnsExpected(
        OverlayStatus status,
        string? overrideLabel,
        string expected
    )
    {
        var result = StatusPill.ResolveLabel(status, overrideLabel);
        Assert.Equal(expected, result);
    }
}
