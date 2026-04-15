using PersonaEngine.Lib.Core.Conversation.Abstractions.Strategies;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Listening.Sections;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.ControlPanel.Listening;

public class InterruptionComposerTests
{
    [Theory]
    [InlineData(false, 1, true, BargeInType.Ignore)]
    [InlineData(false, 5, false, BargeInType.Ignore)]
    [InlineData(true, 1, true, BargeInType.Allow)]
    [InlineData(true, 1, false, BargeInType.NoSpeaking)]
    [InlineData(true, 2, true, BargeInType.MinWords)]
    [InlineData(true, 5, false, BargeInType.MinWordsNoSpeaking)]
    [InlineData(true, 10, true, BargeInType.MinWords)]
    public void ComposeEnum_Covers_AllCases(
        bool enabled,
        int minWords,
        bool allowDuringSpeech,
        BargeInType expected
    )
    {
        var result = InterruptionComposer.Compose(enabled, minWords, allowDuringSpeech);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(BargeInType.Ignore, false, true)]
    [InlineData(BargeInType.Allow, true, true)]
    [InlineData(BargeInType.NoSpeaking, true, false)]
    [InlineData(BargeInType.MinWords, true, true)]
    [InlineData(BargeInType.MinWordsNoSpeaking, true, false)]
    public void Decompose_RoundTrips_ExceptForMinWordsCount(
        BargeInType type,
        bool expectedEnabled,
        bool expectedAllowDuringSpeech
    )
    {
        var (enabled, allowDuringSpeech) = InterruptionComposer.Decompose(type);
        Assert.Equal(expectedEnabled, enabled);
        Assert.Equal(expectedAllowDuringSpeech, allowDuringSpeech);
    }
}
