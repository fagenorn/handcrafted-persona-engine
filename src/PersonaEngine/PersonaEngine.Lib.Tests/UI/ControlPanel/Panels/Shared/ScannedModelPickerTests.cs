using PersonaEngine.Lib.LLM.Connection;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.ControlPanel.Panels.Shared;

public class ScannedModelPickerTests
{
    [Theory]
    [InlineData(LlmProbeStatus.Unknown, "Verify endpoint to list models", true)]
    [InlineData(LlmProbeStatus.Unreachable, "Verify endpoint to list models", true)]
    [InlineData(LlmProbeStatus.Unauthorized, "Verify endpoint to list models", true)]
    [InlineData(LlmProbeStatus.InvalidUrl, "Verify endpoint to list models", true)]
    [InlineData(LlmProbeStatus.Disabled, "Verify endpoint to list models", true)]
    [InlineData(LlmProbeStatus.Probing, "Scanning\u2026", true)]
    [InlineData(LlmProbeStatus.Reachable, "No models reported", true)]
    public void ComputeState_ReturnsExpectedPlaceholderAndEnabled(
        LlmProbeStatus probe,
        string? expectedPlaceholder,
        bool expectedDisabled
    )
    {
        var state = ScannedModelPicker.ComputeState(probe, availableCount: 0);
        Assert.Equal(expectedPlaceholder, state.Placeholder);
        Assert.Equal(expectedDisabled, state.Disabled);
    }

    [Fact]
    public void Reachable_ZeroAvailable_ReturnsNoModelsReportedDisabled()
    {
        var state = ScannedModelPicker.ComputeState(LlmProbeStatus.Reachable, availableCount: 0);

        Assert.Equal("No models reported", state.Placeholder);
        Assert.True(state.Disabled);
    }

    [Fact]
    public void ReachableWithSavedNotInList_ReturnsWarning()
    {
        var state = ScannedModelPicker.ComputeState(
            LlmProbeStatus.Reachable,
            availableCount: 3,
            savedInList: false,
            saved: "gpt-4o"
        );

        Assert.Equal("'gpt-4o' not served by this endpoint \u2014 pick one below", state.Warning);
        Assert.False(state.Disabled);
    }

    [Fact]
    public void ModelMissing_BehavesLikeReachableNotInList()
    {
        var state = ScannedModelPicker.ComputeState(
            LlmProbeStatus.ModelMissing,
            availableCount: 5,
            savedInList: false,
            saved: "gpt-4o"
        );

        Assert.Equal("'gpt-4o' not served by this endpoint \u2014 pick one below", state.Warning);
        Assert.False(state.Disabled);
        Assert.Null(state.Placeholder);
    }
}
