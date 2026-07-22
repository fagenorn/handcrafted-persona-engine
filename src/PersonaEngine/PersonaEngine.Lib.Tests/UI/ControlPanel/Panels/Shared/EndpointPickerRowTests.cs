using PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.ControlPanel.Panels.Shared;

public class EndpointPickerRowTests
{
    [Fact]
    public void DefaultPresets_IncludeAtlasCloudEndpoint()
    {
        var preset = Assert.Single(
            EndpointPickerRow.DefaultPresets,
            item => item.Label == "Atlas Cloud"
        );

        Assert.Equal("https://api.atlascloud.ai/v1", preset.Url);
    }

    [Fact]
    public void DefaultPresets_DoNotDuplicateLabels()
    {
        Assert.Equal(
            EndpointPickerRow.DefaultPresets.Length,
            EndpointPickerRow.DefaultPresets.Select(item => item.Label).Distinct().Count()
        );
    }
}
