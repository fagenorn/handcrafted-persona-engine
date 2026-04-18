using PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.ControlPanel.Panels.Shared;

public class EndpointPickerRowTests
{
    private static readonly IReadOnlyList<(string Label, string Url)> TextPresets =
    [
        ("OpenAI", "https://api.openai.com/v1"),
        ("LM Studio", "http://localhost:1234/v1"),
        ("Ollama", "http://localhost:11434/v1"),
    ];

    [Theory]
    [InlineData("https://api.openai.com/v1", "OpenAI")]
    [InlineData("http://localhost:1234/v1", "LM Studio")]
    [InlineData("http://localhost:11434/v1", "Ollama")]
    [InlineData("HTTPS://api.openai.com/v1/", "OpenAI")]
    public void MatchPreset_ReturnsPresetLabel(string current, string expected)
    {
        Assert.Equal(expected, EndpointPickerRow.MatchPreset(TextPresets, current));
    }

    [Fact]
    public void MatchPreset_UnknownReturnsNull()
    {
        Assert.Null(EndpointPickerRow.MatchPreset(TextPresets, "http://example.com/v1"));
    }
}
