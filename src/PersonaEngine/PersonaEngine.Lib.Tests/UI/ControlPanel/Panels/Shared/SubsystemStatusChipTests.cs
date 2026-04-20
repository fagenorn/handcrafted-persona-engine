using PersonaEngine.Lib.Health;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;
using PersonaEngine.Lib.UI.Overlay;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.ControlPanel.Panels.Shared;

public class SubsystemStatusChipTests
{
    [Theory]
    [InlineData(SubsystemHealth.Healthy, OverlayStatus.Active)]
    [InlineData(SubsystemHealth.Degraded, OverlayStatus.Degraded)]
    [InlineData(SubsystemHealth.Failed, OverlayStatus.Failed)]
    [InlineData(SubsystemHealth.Disabled, OverlayStatus.Off)]
    [InlineData(SubsystemHealth.Muted, OverlayStatus.Muted)]
    [InlineData(SubsystemHealth.Unknown, OverlayStatus.Unknown)]
    public void MapHealth_ProducesExpectedOverlayStatus(
        SubsystemHealth input,
        OverlayStatus expected
    )
    {
        Assert.Equal(expected, SubsystemStatusChip.MapHealth(input));
    }
}
